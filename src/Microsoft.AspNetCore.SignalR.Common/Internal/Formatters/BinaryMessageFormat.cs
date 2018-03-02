// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;

namespace Microsoft.AspNetCore.SignalR.Internal.Formatters
{
    public static class BinaryMessageFormat
    {
        private const int MaxLengthPrefixSize = 5;

        public static void WriteLengthPrefix(long length, IBufferWriter<byte> output)
        {
            // This code writes length prefix of the message as a VarInt. Read the comment in
            // the BinaryMessageParser.TryParseMessage for details.

            var lenBuffer = output.GetSpan(minimumLength: 5);

            var lenNumBytes = 0;
            do
            {
                ref var current = ref lenBuffer[lenNumBytes];
                current = (byte)(length & 0x7f);
                length >>= 7;
                if (length > 0)
                {
                    current |= 0x80;
                }
                lenNumBytes++;
            }
            while (length > 0);

            output.Advance(lenNumBytes);
        }

        public static bool TrySliceMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> payload)
        {
            payload = default;

            if (buffer.IsEmpty)
            {
                return false;
            }

            // The payload starts with a length prefix encoded as a VarInt. VarInts use the most significant bit
            // as a marker whether the byte is the last byte of the VarInt or if it spans to the next byte. Bytes
            // appear in the reverse order - i.e. the first byte contains the least significant bits of the value
            // Examples:
            // VarInt: 0x35 - %00110101 - the most significant bit is 0 so the value is %x0110101 i.e. 0x35 (53)
            // VarInt: 0x80 0x25 - %10000000 %00101001 - the most significant bit of the first byte is 1 so the
            // remaining bits (%x0000000) are the lowest bits of the value. The most significant bit of the second
            // byte is 0 meaning this is last byte of the VarInt. The actual value bits (%x0101001) need to be
            // prepended to the bits we already read so the values is %01010010000000 i.e. 0x1480 (5248)
            // We support paylads up to 2GB so the biggest number we support is 7fffffff which when encoded as
            // VarInt is 0xFF 0xFF 0xFF 0xFF 0x07 - hence the maximum length prefix is 5 bytes.

            var lengthPrefixBuffer = buffer.Slice(0, Math.Min(MaxLengthPrefixSize, buffer.Length));
            long length;
            int lengthByteCount;

            // In order to keep the stackalloc below lexically scoped, we have to call TryParseLengthPrefix in each
            // of the if and else clause.
            if (lengthPrefixBuffer.IsSingleSegment)
            {
                // We can parse the length as a single unit, just slice the first segment and parse it
                if (!TryParseLengthPrefix(lengthPrefixBuffer.First.Span, out lengthByteCount, out length))
                {
                    // Failed to read length prefix!
                    return false;
                }
            }
            else
            {
                // Copy to a stackalloc-ed buffer so we have a single contiguous buffer
                Span<byte> stackBuf = stackalloc byte[5];
                lengthPrefixBuffer.CopyTo(stackBuf);

                if (!TryParseLengthPrefix(stackBuf, out lengthByteCount, out length))
                {
                    // Failed to read length prefix!
                    return false;
                }
            }

            // We don't have enough data
            if (buffer.Length < length + lengthByteCount)
            {
                return false;
            }

            // Get the payload
            payload = buffer.Slice(lengthByteCount, length);

            // Skip the payload
            buffer = buffer.Slice(lengthByteCount + length);
            return true;
        }

        private static bool TryParseLengthPrefix(ReadOnlySpan<byte> lengthSpan, out int bytesRead, out long length)
        {
            length = 0;
            bytesRead = 0;

            if (lengthSpan.Length == 0)
            {
                return false;
            }

            byte lengthByte;
            do
            {
                lengthByte = lengthSpan[bytesRead];
                length = length | (((uint)(lengthByte & 0x7f)) << (bytesRead * 7));
                bytesRead += 1;
            }
            while (bytesRead < lengthSpan.Length && ((lengthByte & 0x80) != 0));

            // size bytes are missing
            if ((lengthByte & 0x80) != 0 && (bytesRead < MaxLengthPrefixSize))
            {
                // "unread" the bytes we read
                bytesRead = 0;
                length = 0;
                return false;
            }

            if ((lengthByte & 0x80) != 0 || (bytesRead == MaxLengthPrefixSize && lengthByte > 7))
            {
                throw new FormatException("Messages over 2GB in size are not supported.");
            }

            return true;
        }
    }
}
