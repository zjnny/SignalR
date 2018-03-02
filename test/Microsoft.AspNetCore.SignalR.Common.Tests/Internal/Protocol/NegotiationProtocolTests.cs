// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class NegotiationProtocolTests
    {
        public static IEnumerable<object[]> ProtocolTestData = new[]
        {
            new object[] { "{\"protocol\":\"json\"}\u001e" , "json" },
            new object[] { "{\"protocol\":\"messagepack\"}\u001e" , "messagepack" },
        };

        [Theory]
        [MemberData(nameof(ProtocolTestData))]
        public void CanParseNegotiateMessage(string json, string expectedProtocol)
        {
            // Add some padding to detect the slicing behavior
            var array = new byte[Encoding.UTF8.GetByteCount(json) + 2];
            Encoding.UTF8.GetBytes(json, 0, json.Length, array, 0);
            array[array.Length - 2] = 42;
            array[array.Length - 1] = 24;

            var buffer = new ReadOnlySequence<byte>(array);
            NegotiationProtocol.TryParseMessage(ref buffer, out var message);
            Assert.NotNull(message);
            Assert.Equal(expectedProtocol, message.Protocol);
            Assert.Equal(new byte[] { 42, 24 }, buffer.ToArray());
        }

        [Theory]
        [MemberData(nameof(ProtocolTestData))]
        public async Task CanWriteNegotiateMessageAsync(string expectedJson, string protocol)
        {
            // TODO: Convert this to MemoryBufferWriter when it's written.
            var pipe = new Pipe();
            var message = new NegotiationMessage(protocol);
            NegotiationProtocol.WriteMessage(message, pipe.Writer);
            await pipe.Writer.FlushAsync();
            pipe.Writer.Complete();

            var result = await pipe.Reader.ReadAllAsync();
            Assert.Equal(expectedJson, Encoding.UTF8.GetString(result));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{\"")]
        [InlineData("{\"protocol\":")]
        [InlineData("{\"protocol\":\"json\"}")]
        public void ReturnsFalseAndDoesNotAdvanceBufferForIncompletePayloads(string payload)
        {
            var message = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(payload));
            var oldLength = message.Length;

            Assert.False(NegotiationProtocol.TryParseMessage(ref message, out var result));
            Assert.Null(result);
            Assert.Equal(oldLength, message.Length);
        }

        [Theory]
        [InlineData("\u001e", "Error reading JToken from JsonReader. Path '', line 0, position 0.")]
        public void ParsingNegotiationMessageThrowsForInvalidJson(string payload, string expectedMessage)
        {
            var message = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(payload));

            var exception = Assert.Throws<JsonReaderException>(() =>
                NegotiationProtocol.TryParseMessage(ref message, out var deserializedMessage));

            Assert.Equal(expectedMessage, exception.Message);
        }

        [Theory]
        [InlineData("42\u001e", "Unexpected JSON Token Type 'Integer'. Expected a JSON Object.")]
        [InlineData("\"42\"\u001e", "Unexpected JSON Token Type 'String'. Expected a JSON Object.")]
        [InlineData("null\u001e", "Unexpected JSON Token Type 'Null'. Expected a JSON Object.")]
        [InlineData("{}\u001e", "Missing required property 'protocol'.")]
        [InlineData("[]\u001e", "Unexpected JSON Token Type 'Array'. Expected a JSON Object.")]
        public void ParsingNegotiationMessageThrowsForInvalidMessages(string payload, string expectedMessage)
        {
            var message = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(payload));

            var exception = Assert.Throws<InvalidDataException>(() =>
                NegotiationProtocol.TryParseMessage(ref message, out var deserializedMessage));

            Assert.Equal(expectedMessage, exception.Message);
        }
    }
}
