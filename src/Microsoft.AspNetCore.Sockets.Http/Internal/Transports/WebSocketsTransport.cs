// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Http.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Internal.Transports
{
    public class WebSocketsTransport : IHttpTransport
    {
        private readonly WebSocketOptions _options;
        private readonly ILogger _logger;
        private readonly IPipeReader _application;
        private readonly DefaultConnectionContext _connection;

        public WebSocketsTransport(WebSocketOptions options, IPipeReader application, DefaultConnectionContext connection, ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _options = options;
            _application = application;
            _connection = connection;
            _logger = loggerFactory.CreateLogger<WebSocketsTransport>();
        }

        public async Task ProcessRequestAsync(HttpContext context, CancellationToken token)
        {
            Debug.Assert(context.WebSockets.IsWebSocketRequest, "Not a websocket request");

            using (var ws = await context.WebSockets.AcceptWebSocketAsync(_options.SubProtocol))
            {
                Log.SocketOpened(_logger, null);

                try
                {
                    await ProcessSocketAsync(ws);
                }
                finally
                {
                    Log.SocketClosed(_logger, null);
                }
            }
        }

        public async Task ProcessSocketAsync(WebSocket socket)
        {
            // Begin sending and receiving. Receiving must be started first because ExecuteAsync enables SendAsync.
            var receiving = StartReceiving(socket);
            var sending = StartSending(socket);

            // Wait for something to shut down.
            var trigger = await Task.WhenAny(
                receiving,
                sending);

            var failed = trigger.IsCanceled || trigger.IsFaulted;
            var task = Task.CompletedTask;
            if (trigger == receiving)
            {
                task = sending;
                Log.WaitingForSend(_logger, null);
            }
            else
            {
                task = receiving;
                Log.WaitingForClose(_logger, null);
            }

            // We're done writing
            _application.Writer.TryComplete();

            await socket.CloseOutputAsync(failed ? WebSocketCloseStatus.InternalServerError : WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

            var resultTask = await Task.WhenAny(task, Task.Delay(_options.CloseTimeout));

            if (resultTask != task)
            {
                Log.CloseTimedOut(_logger, null);
                socket.Abort();
            }
            else
            {
                // Observe any exceptions from second completed task
                task.GetAwaiter().GetResult();
            }

            // Observe any exceptions from original completed task
            trigger.GetAwaiter().GetResult();
        }

        private async Task<WebSocketReceiveResult> StartReceiving(WebSocket socket, CancellationToken cancellationToken)
        {
            // REVIEW: This code was copied from the client, it's highly unoptimized at the moment (especially
            // for server logic)
            var incomingMessage = new List<ArraySegment<byte>>();
            while (true)
            {
                const int bufferSize = 4096;

                // REVIEW: Should we use a buffer size at all?
                // REVIEW: This will disregard EndOfMessage.
                var buffer = _connection.Application.Writer.Alloc(bufferSize);

                // Exceptions are handled above where the send and receive tasks are being run.
                var receiveResult = await socket.ReceiveMemoryAsync(buffer.Buffer, cancellationToken);

                // If the message was a close frame, terminate (close frame payload isn't written through)
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    return receiveResult;
                }

                Log.MessageReceived(_logger, receiveResult.MessageType, receiveResult.Count, receiveResult.EndOfMessage, null);

                // Commit and flush the write
                buffer.Commit();
                await buffer.FlushAsync();

                var truncBuffer = new ArraySegment<byte>(buffer.Array, 0, receiveResult.Count);
                incomingMessage.Add(truncBuffer);
                totalBytes += receiveResult.Count;

                // Making sure the message type is either text or binary
                Debug.Assert((receiveResult.MessageType == WebSocketMessageType.Binary || receiveResult.MessageType == WebSocketMessageType.Text), "Unexpected message type");

                // TODO: Check received message type against the _options.WebSocketMessageType

                byte[] messageBuffer = null;

                if (incomingMessage.Count > 1)
                {
                    messageBuffer = new byte[totalBytes];
                    var offset = 0;
                    for (var i = 0; i < incomingMessage.Count; i++)
                    {
                        Buffer.BlockCopy(incomingMessage[i].Array, 0, messageBuffer, offset, incomingMessage[i].Count);
                        offset += incomingMessage[i].Count;
                    }
                }
                else
                {
                    messageBuffer = new byte[incomingMessage[0].Count];
                    Buffer.BlockCopy(incomingMessage[0].Array, incomingMessage[0].Offset, messageBuffer, 0, incomingMessage[0].Count);
                }

                Log.MessageToApplication(_logger, messageBuffer.Length, null);
                while (await _application.Writer.WaitToWriteAsync())
                {
                    if (_application.Writer.TryWrite(messageBuffer))
                    {
                        incomingMessage.Clear();
                        break;
                    }
                }
            }
        }

        private async Task StartSending(WebSocket ws)
        {
            while (await _application.Reader.WaitToReadAsync())
            {
                // Get a frame from the application
                while (_application.Reader.TryRead(out var buffer))
                {
                    if (buffer.Length > 0)
                    {
                        try
                        {
                            Log.SendPayload(_logger, buffer.Length);

                            var webSocketMessageType = (_connection.TransferMode == TransferMode.Binary
                                ? WebSocketMessageType.Binary
                                : WebSocketMessageType.Text);

                            if (WebSocketCanSend(ws))
                            {
                                await ws.SendAsync(new ArraySegment<byte>(buffer), webSocketMessageType, endOfMessage: true, cancellationToken: CancellationToken.None);
                            }
                        }
                        catch (WebSocketException socketException) when (!WebSocketCanSend(ws))
                        {
                            // this can happen when we send the CloseFrame to the client and try to write afterwards
                            Log.SendFailed(_logger, socketException);
                            break;
                        }
                        catch (Exception ex)
                        {
                            Log.ErrorWritingFrame(_logger, ex);
                            break;
                        }
                    }
                }
            }
        }

        private static bool WebSocketCanSend(WebSocket ws)
        {
            return !(ws.State == WebSocketState.Aborted ||
                   ws.State == WebSocketState.Closed ||
                   ws.State == WebSocketState.CloseSent);
        }

        private static class Log
        {
            public static readonly Action<ILogger, Exception> SocketOpened =
                LoggerMessage.Define(LogLevel.Information, new EventId(1, "SocketOpened"), "Socket opened.");

            public static readonly Action<ILogger, Exception> SocketClosed =
                LoggerMessage.Define(LogLevel.Information, new EventId(2, "SocketClosed"), "Socket closed.");

            public static readonly Action<ILogger, WebSocketCloseStatus?, string, Exception> ClientClosed =
                LoggerMessage.Define<WebSocketCloseStatus?, string>(LogLevel.Debug, new EventId(3, "ClientClosed"), "Client closed connection with status code '{status}' ({description}). Signaling end-of-input to application.");

            public static readonly Action<ILogger, Exception> WaitingForSend =
                LoggerMessage.Define(LogLevel.Debug, new EventId(4, "WaitingForSend"), "Waiting for the application to finish sending data.");

            public static readonly Action<ILogger, Exception> FailedSending =
                LoggerMessage.Define(LogLevel.Debug, new EventId(5, "FailedSending"), "Application failed during sending. Sending InternalServerError close frame.");

            public static readonly Action<ILogger, Exception> FinishedSending =
                LoggerMessage.Define(LogLevel.Debug, new EventId(6, "FinishedSending"), "Application finished sending. Sending close frame.");

            public static readonly Action<ILogger, Exception> WaitingForClose =
                LoggerMessage.Define(LogLevel.Debug, new EventId(7, "WaitingForClose"), "Waiting for the client to close the socket.");

            public static readonly Action<ILogger, Exception> CloseTimedOut =
                LoggerMessage.Define(LogLevel.Debug, new EventId(8, "CloseTimedOut"), "Timed out waiting for client to send the close frame, aborting the connection.");

            public static readonly Action<ILogger, WebSocketMessageType, int, bool, Exception> MessageReceived =
                LoggerMessage.Define<WebSocketMessageType, int, bool>(LogLevel.Debug, new EventId(9, "MessageReceived"), "Message received. Type: {messageType}, size: {size}, EndOfMessage: {endOfMessage}.");

            public static readonly Action<ILogger, int, Exception> MessageToApplication =
                LoggerMessage.Define<int>(LogLevel.Debug, new EventId(10, "MessageToApplication"), "Passing message to application. Payload size: {size}.");

            public static readonly Action<ILogger, int, Exception> SendPayload =
                LoggerMessage.Define<int>(LogLevel.Debug, new EventId(11, "SendPayload"), "Sending payload: {size} bytes.");

            public static readonly Action<ILogger, Exception> ErrorWritingFrame =
                LoggerMessage.Define(LogLevel.Error, new EventId(12, "ErrorWritingFrame"), "Error writing frame.");

            public static readonly Action<ILogger, Exception> SendFailed =
                LoggerMessage.Define(LogLevel.Trace, new EventId(13, "SendFailed"), "Socket failed to send.");
        }
    }
}
