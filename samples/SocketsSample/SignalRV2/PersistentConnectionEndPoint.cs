// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Protocols.Features;
using Microsoft.AspNetCore.Sockets;
using Newtonsoft.Json;

namespace SocketsSample
{
    public class PersistentConnectionEndPoint : EndPoint
    {
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            // Send the init payload
            await connection.Transport.Output.WriteAsync(GetJsonBytes(new
            {
                M = new object[0],
                S = 1
            }));

            if (connection.Features.Get<IConnectionInherentKeepAliveFeature>() == null)
            {
                connection.Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(state =>
                {
                    // Keep alive
                    // _ = connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("{}"));
                },
                null);
            }

            while (true)
            {
                var result = await connection.Transport.Input.ReadAsync();
                var buffer = result.Buffer;

                if (!buffer.IsEmpty)
                {
                    System.Console.WriteLine($"Received: {buffer.Length} bytes");

                    await connection.Transport.Output.WriteAsync(GetJsonBytes(
                        new
                        {
                            M = new[] { Encoding.UTF8.GetString(buffer.ToArray()) }
                        }
                    ));
                }
                else if (result.IsCompleted)
                {
                    break;
                }
                connection.Transport.Input.AdvanceTo(buffer.End);
            }
        }

        private byte[] GetJsonBytes(object o)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o));
        }
    }
}
