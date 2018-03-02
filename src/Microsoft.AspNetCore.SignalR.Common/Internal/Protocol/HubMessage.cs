// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Buffers;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public abstract class HubMessage
    {
        protected HubMessage()
        {
        }

        // Initialize with capacity 4 for the 2 built in protocols and 2 data encoders
        private readonly List<SerializedMessage> _serializedMessages = new List<SerializedMessage>(4);

        public void WriteMessage(IOutput output, IHubProtocol protocol)
        {
            for (var i = 0; i < _serializedMessages.Count; i++)
            {
                if (ReferenceEquals(_serializedMessages[i].Protocol, protocol))
                {
                    output.Write(_serializedMessages[i].Message);
                }
            }

            var bytes = protocol.WriteMessage(hubMessage: this);

            // We don't want to balloon memory if someone writes a poor IHubProtocolResolver
            // So we cap how many caches we store and worst case just serialize the message for every connection
            if (_serializedMessages.Count < 10)
            {
                _serializedMessages.Add(new SerializedMessage(protocol, bytes));
            }

            return bytes;
        }

        private readonly struct SerializedMessage
        {
            public readonly IHubProtocol Protocol;
            public readonly byte[] Message;

            public SerializedMessage(IHubProtocol protocolReaderWriter, byte[] message)
            {
                Protocol = protocolReaderWriter;
                Message = message;
            }
        }
    }
}
