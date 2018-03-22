// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace SocketsSample
{
    public static class SignalRV2RouteBuilderExtensions
    {
        public static SocketRouteBuilder MapSignalRV2EndPoint(this SocketRouteBuilder routes, PathString path, Action<IConnectionBuilder> connectionConfig)
        {
            var connectionBuilder = new ConnectionBuilder(routes.ServiceProvider);
            connectionConfig(connectionBuilder);
            var socket = connectionBuilder.Build();

            var dispatcher = routes.ServiceProvider.GetRequiredService<SignalRV2Dispatcher>();
            var options = new HttpSocketOptions();

            routes.RouteBuilder.MapRoute(path + "/negotiate", c => dispatcher.ExecuteNegotiateAsync(c, options, socket));
            routes.RouteBuilder.MapRoute(path + "/abort", c => dispatcher.ExecuteAbortAsync(c, options, socket));
            routes.RouteBuilder.MapRoute(path + "/connect", c => dispatcher.ExecuteConnectAsync(c, options, socket));
            routes.RouteBuilder.MapRoute(path + "/send", c => dispatcher.ExecuteSendAsync(c, options, socket));
            routes.RouteBuilder.MapRoute(path + "/start", c => dispatcher.ExecuteStartAsync(c, options, socket));
            routes.RouteBuilder.MapRoute(path + "/reconnect", c => dispatcher.ExecuteReconnectAsync(c, options, socket));
            routes.RouteBuilder.MapRoute(path + "/ping", c => dispatcher.ExecutePingAsync(c, options, socket));

            return routes;
        }
    }
}