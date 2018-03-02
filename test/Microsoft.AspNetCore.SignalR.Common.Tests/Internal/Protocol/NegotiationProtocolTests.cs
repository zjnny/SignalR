// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class NegotiationProtocolTests
    {
        [Theory]
        [InlineData("{ \"protocol\": \"json\" }\u001e", "json")]
        [InlineData("{ \"protocol\": \"messagepack\" }\u001e", "messagepack")]
        public async Task CanWriteNegotiation(string expectedJson, string protocol)
        {
            var message = new NegotiationMessage(protocol);
            var bytes = await MemoryOutput.GetOutputAsArrayAsync(output => NegotiationProtocol.WriteMessage(message, output));
            Assert.Equal(expectedJson, Encoding.UTF8.GetString(bytes));
        }

        [Theory]
        [InlineData("{ \"protocol\": \"json\" }\u001e", "json")]
        [InlineData("{ \"protocol\": \"messagepack\" }\u001e", "messagepack")]
        public void CanParseNegotiation(string json, string expectedProtocol)
        {
            var buffer = new ReadOnlyBuffer<byte>(Encoding.UTF8.GetBytes(json));
            Assert.True(NegotiationProtocol.TryParseMessage(ref buffer, out var deserializedMessage));

            Assert.NotNull(deserializedMessage);
            Assert.Equal(expectedProtocol, deserializedMessage.Protocol);
        }

        [Theory]
        [InlineData("", "Unable to parse payload as a negotiation message.")]
        [InlineData("42\u001e", "Unexpected JSON Token Type 'Integer'. Expected a JSON Object.")]
        [InlineData("\"42\"\u001e", "Unexpected JSON Token Type 'String'. Expected a JSON Object.")]
        [InlineData("null\u001e", "Unexpected JSON Token Type 'Null'. Expected a JSON Object.")]
        [InlineData("{}\u001e", "Missing required property 'protocol'.")]
        [InlineData("[]\u001e", "Unexpected JSON Token Type 'Array'. Expected a JSON Object.")]
        public void ParsingNegotiationMessageThrowsForInvalidMessages(string payload, string expectedMessage)
        {
            var message = new ReadOnlyBuffer<byte>(Encoding.UTF8.GetBytes(payload));

            var exception = Assert.Throws<InvalidDataException>(() =>
                Assert.True(NegotiationProtocol.TryParseMessage(ref message, out var deserializedMessage)));

            Assert.Equal(expectedMessage, exception.Message);
        }
    }
}
