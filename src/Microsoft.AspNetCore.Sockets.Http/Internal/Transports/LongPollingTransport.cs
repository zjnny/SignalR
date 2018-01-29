// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Features;
using Microsoft.AspNetCore.Sockets.Http.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Internal.Transports
{
    public class LongPollingTransport : IHttpTransport
    {
        private readonly IPipeReader _application;
        private readonly ILogger _logger;
        private readonly CancellationToken _timeoutToken;
        private readonly string _connectionId;

        public LongPollingTransport(CancellationToken timeoutToken, IPipeReader application, string connectionId, ILoggerFactory loggerFactory)
        {
            _timeoutToken = timeoutToken;
            _application = application;
            _connectionId = connectionId;
            _logger = loggerFactory.CreateLogger<LongPollingTransport>();
        }

        public async Task ProcessRequestAsync(HttpContext context, CancellationToken token)
        {
            try
            {
                var result = await _application.ReadAsync(_timeoutToken);
                if (result.IsCancelled || result.IsCompleted)
                {
                    if (result.IsCancelled && _timeoutToken.IsCancellationRequested)
                    {
                        // Timeout elapsed while waiting for a message. Just return 200 OK with no content.
                        Log.TimedOut(_logger, null);

                        context.Response.ContentLength = 0;
                        context.Response.ContentType = "text/plain";
                        context.Response.StatusCode = StatusCodes.Status200OK;
                    }
                    else if (result.IsCancelled && context.RequestAborted.IsCancellationRequested)
                    {
                        // Don't count this as cancellation, this is normal as the poll can end due to the browser closing.
                        // The background thread will eventually dispose this connection if it's inactive
                        Log.Disconnected(_logger, null);
                    }
                    else
                    {
                        // Either a completion or an unexpected cancellation occurred
                        Log.Completed(_logger, null);
                        context.Response.ContentType = "text/plain";
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                        return;
                    }
                }

                context.Response.ContentLength = result.Buffer.Length;
                context.Response.ContentType = "application/octet-stream";
                Log.WritingMessage(_logger, result.Buffer.Length, null);

                // Intentionally not using cancellation because we need to flush the messages out and can't send a 204 anymore (headers may have been sent)
                foreach (var buffer in result.Buffer)
                {
                    await context.Response.Body.WriteMemoryAsync(buffer);
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
            public static readonly Action<ILogger, Exception> Completed =
                LoggerMessage.Define(LogLevel.Information, new EventId(1, "Completed"), "Terminating Long Polling connection by sending 204 response.");

            public static readonly Action<ILogger, Exception> TimedOut =
                LoggerMessage.Define(LogLevel.Information, new EventId(2, "TimedOut"), "Poll request timed out. Sending 200 response to connection.");

            public static readonly Action<ILogger, long, Exception> WritingMessage =
                LoggerMessage.Define<long>(LogLevel.Debug, new EventId(3, "WritingMessage"), "Writing a {count} byte message to connection.");

            public static readonly Action<ILogger, Exception> Disconnected =
                LoggerMessage.Define(LogLevel.Debug, new EventId(4, "Disconnected"), "Client disconnected from Long Polling endpoint for connection.");

            public static readonly Action<ILogger, Exception> Terminated =
                LoggerMessage.Define(LogLevel.Error, new EventId(5, "Terminated"), "Long Polling transport was terminated due to an error.");
        }
    }
}
