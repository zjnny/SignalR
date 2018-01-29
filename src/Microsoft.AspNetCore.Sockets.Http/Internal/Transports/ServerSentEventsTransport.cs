// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Internal.Transports
{
    public class ServerSentEventsTransport : IHttpTransport
    {
        private readonly IPipeReader _application;
        private readonly string _connectionId;
        private readonly ILogger _logger;

        public ServerSentEventsTransport(IPipeReader application, string connectionId, ILoggerFactory loggerFactory)
        {
            _application = application;
            _connectionId = connectionId;
            _logger = loggerFactory.CreateLogger<ServerSentEventsTransport>();
        }

        public async Task ProcessRequestAsync(HttpContext context, CancellationToken token)
        {
            try
            {
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers["Cache-Control"] = "no-cache";

                // Make sure we disable all response buffering for SSE
                var bufferingFeature = context.Features.Get<IHttpBufferingFeature>();
                bufferingFeature?.DisableResponseBuffering();

                context.Response.Headers["Content-Encoding"] = "identity";

                // Workaround for a Firefox bug where EventSource won't fire the open event
                // until it receives some data
                await context.Response.WriteAsync(":\r\n");
                await context.Response.Body.FlushAsync();

                while (true)
                {
                    var result = await _application.ReadAsync();
                    if (result.IsCancelled || result.IsCompleted)
                    {
                        Log.ConnectionClosed(_logger, null);
                        break;
                    }

                    // Write each of the segments in the buffer
                    foreach (var segment in result.Buffer)
                    {
                        Log.WritingMessage(_logger, segment.Length, null);
                        ServerSentEventsMessageFormatter.WriteMessage(segment.Span, context.Response.Body);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Terminated(_logger, ex);
                throw;
            }
        }

        private static class Log
        {
            public static readonly Action<ILogger, Exception> ConnectionClosed =
                LoggerMessage.Define(LogLevel.Trace, new EventId(1, "ConnectionClosed"), "The connection has been closed.");

            public static readonly Action<ILogger, int, Exception> WritingMessage =
                LoggerMessage.Define<int>(LogLevel.Debug, new EventId(2, "WritingMessage"), "Writing a {count} byte message.");

            public static readonly Action<ILogger, Exception> Terminated =
                LoggerMessage.Define(LogLevel.Error, new EventId(3, "Terminated"), "Server-sent events transport was terminated due to an error.");
        }
    }
}
