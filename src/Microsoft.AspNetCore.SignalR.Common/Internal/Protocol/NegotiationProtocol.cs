// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public static class NegotiationProtocol
    {
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private const string ProtocolPropertyName = "protocol";

        public static void WriteMessage(NegotiationMessage negotiationMessage, IBufferWriter<byte> output)
        {
            // TODO: Another place to use the IOutput stream wrapper
            using (var ms = new MemoryStream())
            {
                using (var writer = new JsonTextWriter(new StreamWriter(ms, _utf8NoBom, 1024, leaveOpen: true)))
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName(ProtocolPropertyName);
                    writer.WriteValue(negotiationMessage.Protocol);
                    writer.WriteEndObject();
                }
                ms.Flush();

                output.Write(ms.GetBuffer().AsReadOnlySpan().Slice(0, (int)ms.Length));
            }

            TextMessageFormat.WriteRecordSeparator(output);
        }

        public static bool TryParseMessage(ref ReadOnlySequence<byte> input, out NegotiationMessage negotiationMessage)
        {
            if (!TextMessageFormat.TrySliceMessage(ref input, out var message))
            {
                // Haven't seen the entire negotiate message so bail
                negotiationMessage = null;
                return false;
            }
            else
            {
                using (var memoryStream = new MemoryStream(message.ToArray()))
                {
                    using (var reader = new JsonTextReader(new StreamReader(memoryStream)))
                    {
                        var token = JToken.ReadFrom(reader);
                        if (token == null || token.Type != JTokenType.Object)
                        {
                            throw new InvalidDataException($"Unexpected JSON Token Type '{token?.Type}'. Expected a JSON Object.");
                        }

                        var negotiationJObject = (JObject)token;
                        var protocol = JsonUtils.GetRequiredProperty<string>(negotiationJObject, ProtocolPropertyName);
                        negotiationMessage = new NegotiationMessage(protocol);
                    }
                }
                return true;
            }
        }
    }
}
