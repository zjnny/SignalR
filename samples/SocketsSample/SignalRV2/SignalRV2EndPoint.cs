// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SocketsSample.Hubs;

namespace SocketsSample
{
    public class SignalRV2EndPoint : EndPoint
    {
        private IServiceProvider _provider;

        private ConcurrentDictionary<string, SignalRV2HubProtocol> _protocols = new ConcurrentDictionary<string, SignalRV2HubProtocol>();

        public SignalRV2EndPoint(IServiceProvider provider)
        {
            _provider = provider;
        }

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            // Parse the data as a JArray hub names and get the correct hub dispatcher
            // based on the hub name
            var data = connection.Items["connectionData"];

            // TODO: Don't hardcode the hub type here
            var hubDispatcherType = typeof(HubDispatcher<>).MakeGenericType(typeof(Chat));

            var hubConnectionContext = new HubConnectionContext(connection, Timeout.InfiniteTimeSpan, _provider.GetRequiredService<ILoggerFactory>());

            // TODO: Don't hardcode the hub name and hub
            hubConnectionContext.Protocol = _protocols.GetOrAdd("chat", key => new SignalRV2HubProtocol(key));

            var dispatcher = (HubDispatcher)_provider.GetRequiredService(hubDispatcherType);

            // Raise OnConnected
            await dispatcher.OnConnectedAsync(hubConnectionContext);

            // Send the start message
            await connection.Transport.Output.WriteAsync(GetJsonBytes(new
            {
                M = new object[0],
                S = 1
            }));

            while (true)
            {
                var result = await connection.Transport.Input.ReadAsync();
                var buffer = result.Buffer;

                if (!buffer.IsEmpty)
                {
                    var messages = new List<HubMessage>();
                    if (hubConnectionContext.Protocol.TryParseMessages(buffer.ToArray(), dispatcher, messages))
                    {
                        foreach (var message in messages)
                        {
                            await dispatcher.DispatchMessageAsync(hubConnectionContext, message);
                        }
                    }

                }
                else if (result.IsCompleted)
                {
                    break;
                }

                connection.Transport.Input.AdvanceTo(buffer.End);
            }

            // TODO: Handle application errors
            await dispatcher.OnDisconnectedAsync(hubConnectionContext, null);
        }

        private byte[] GetJsonBytes(object o)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o));
        }
    }
}
