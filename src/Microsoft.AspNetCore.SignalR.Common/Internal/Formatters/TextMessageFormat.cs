// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Buffers;

namespace Microsoft.AspNetCore.SignalR.Internal.Formatters
{
    public static class TextMessageFormat
    {
        // This record separator is supposed to be used only for JSON payloads where 0x1e character
        // will not occur (is not a valid character) and therefore it is safe to not escape it
        public static readonly byte RecordSeparator = 0x1e;

        // In array form for WriteRecordSeparator to use
        private static readonly byte[] RecordSeparatorArray = new byte[] { RecordSeparator };

        /// <summary>
        /// Tries to slice a message out of the buffer by scanning for the <see cref="RecordSeparator"/> character.
        /// </summary>
        /// <param name="buffer">The buffer from which to slice out the message. If a message is found, this buffer is sliced to start AFTER the record separator.</param>
        /// <param name="message">If this method returns true, this is a <see cref="ReadOnlyBuffer{T}"/> containing the message</param>
        /// <returns>true if the record separator was found and a message was sliced, false if not (and the buffer has been left unchanged)</returns>
        public static bool TrySliceMessage(ref ReadOnlyBuffer<byte> buffer, out ReadOnlyBuffer<byte> message)
        {
            var pos = buffer.PositionOf(RecordSeparator);
            if(pos == null)
            {
                message = ReadOnlyBuffer<byte>.Empty;
                return false;
            }
            else
            {
                message = buffer.Slice(buffer.Start, pos.Value);
                buffer = buffer.Slice(buffer.GetPosition(pos.Value, 1));
                return true;
            }
        }

        /// <summary>
        /// Writes the record separator to the provided buffer
        /// </summary>
        /// <param name="output">The buffer to write to</param>
        public static void WriteRecordSeparator(IOutput output) => output.Write(RecordSeparatorArray);
    }
}
