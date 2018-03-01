// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Formatters
{
    public class TextMessageFormatTests
    {
        [Fact]
        public async Task WriteMessage()
        {
            const string message = "ABC";

            var pipe = new Pipe();
            pipe.Writer.Write(Encoding.UTF8.GetBytes(message));
            TextMessageFormat.WriteRecordSeparator(pipe.Writer);
            await pipe.Writer.FlushAsync();
            pipe.Writer.Complete();

            Assert.Equal("ABC\u001e", Encoding.UTF8.GetString(await pipe.Reader.ReadAllAsync()));
        }

        [Fact]
        public void ReadMessage()
        {
            var buffer = new ReadOnlyBuffer<byte>(Encoding.UTF8.GetBytes("ABC\u001e"));

            Assert.True(TextMessageFormat.TrySliceMessage(ref buffer, out var payload));
            Assert.Equal("ABC", Encoding.UTF8.GetString(payload.ToArray()));
            Assert.False(TextMessageFormat.TrySliceMessage(ref buffer, out payload));
        }

        [Fact]
        public void TryReadingIncompleteMessage()
        {
            var buffer = new ReadOnlyBuffer<byte>(Encoding.UTF8.GetBytes("ABC"));
            var oldStart = buffer.Start;
            Assert.False(TextMessageFormat.TrySliceMessage(ref buffer, out var payload));
            Assert.Equal(oldStart, buffer.Start);
        }

        [Fact]
        public void TryReadingMultipleMessages()
        {
            var buffer = new ReadOnlyBuffer<byte>(Encoding.UTF8.GetBytes("ABC\u001eXYZ\u001e"));
            Assert.True(TextMessageFormat.TrySliceMessage(ref buffer, out var payload));
            Assert.Equal("ABC", Encoding.UTF8.GetString(payload.ToArray()));
            Assert.True(TextMessageFormat.TrySliceMessage(ref buffer, out payload));
            Assert.Equal("XYZ", Encoding.UTF8.GetString(payload.ToArray()));
        }

        [Fact]
        public void IncompleteTrailingMessage()
        {
            var buffer = new ReadOnlyBuffer<byte>(Encoding.UTF8.GetBytes("ABC\u001eXYZ\u001e123"));
            Assert.True(TextMessageFormat.TrySliceMessage(ref buffer, out var payload));
            Assert.Equal("ABC", Encoding.UTF8.GetString(payload.ToArray()));
            Assert.True(TextMessageFormat.TrySliceMessage(ref buffer, out payload));
            Assert.Equal("XYZ", Encoding.UTF8.GetString(payload.ToArray()));
            Assert.False(TextMessageFormat.TrySliceMessage(ref buffer, out payload));
        }
    }
}
