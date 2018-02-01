using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Client.Tests;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.AspNetCore.Sockets.Internal.Transports;
using Microsoft.AspNetCore.Sockets.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace NonParallelTests
{
    [CollectionDefinition("test", DisableParallelization = true)]
    public class SomeCollection
    {
    }

    [Collection("test")]
    public class Class1 : LoggedTest
    {
        public Class1(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task TransportFailsOnTimeoutWithErrorWhenApplicationFailsAndClientDoesNotSendCloseFrame()
        {
            await Task.Run(async () =>
            {
                using (StartLog(out var loggerFactory, LogLevel.Trace))
                {
                    var transportToApplication = Channel.CreateUnbounded<byte[]>();
                    var applicationToTransport = Channel.CreateUnbounded<byte[]>();

                    using (var transportSide = ChannelConnection.Create<byte[]>(applicationToTransport, transportToApplication))
                    using (var applicationSide = ChannelConnection.Create<byte[]>(transportToApplication, applicationToTransport))
                    using (var feature = new TestWebSocketConnectionFeature())
                    {
                        var options = new WebSocketOptions
                        {
                            CloseTimeout = TimeSpan.FromSeconds(1)
                        };

                        var connectionContext = new DefaultConnectionContext(string.Empty, null, null);
                        var ws = new WebSocketsTransport(options, transportSide, connectionContext, loggerFactory);

                        var serverSocket = await feature.AcceptAsync();
                        // Give the server socket to the transport and run it
                        var transport = ws.ProcessSocketAsync(serverSocket);

                        // Run the client socket
                        var client = feature.Client.ExecuteAndCaptureFramesAsync();

                        // fail the client to server channel
                        applicationToTransport.Writer.TryComplete(new Exception());

                        await Assert.ThrowsAsync<Exception>(() => transport).OrTimeout();

                        Assert.Equal(WebSocketState.Aborted, serverSocket.State);
                    }
                }
            });
        }

        [Fact]
        public async Task ConnectionTerminatedIfServerTimeoutIntervalElapsesWithNoMessages()
        {
            //await Task.Run(async () =>
            //{
                using (StartLog(out var loggerFactory, LogLevel.Trace))
                {
                    var connection = new TestConnection();
                    var hubConnection = new HubConnection(connection, new JsonHubProtocol(), loggerFactory);

                    hubConnection.ServerTimeout = TimeSpan.FromMilliseconds(100);

                    await hubConnection.StartAsync().OrTimeout();

                    var closeTcs = new TaskCompletionSource<Exception>();
                hubConnection.Closed += ex =>
                {
                    loggerFactory.CreateLogger("intest").LogError("closed called");
                    closeTcs.TrySetResult(ex);
                };
                    var exception = Assert.IsType<TimeoutException>(await closeTcs.Task.OrTimeout());
                    Assert.Equal("Server timeout (100.00ms) elapsed without receiving a message from the server.", exception.Message);
                }
            //});
        }

        [Fact]
        public async Task WebSocketTransportTimesOutWhenCloseFrameNotReceived()
        {
            await Task.Run(async () =>
            {
                using (StartLog(out var loggerFactory, LogLevel.Trace))
                {
                    var manager = CreateConnectionManager();
                    var connection = manager.CreateConnection();

                    var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                    var context = MakeRequest("/foo", connection);
                    SetTransport(context, TransportType.WebSockets);

                    var services = new ServiceCollection();
                    services.AddEndPoint<ImmediatelyCompleteEndPoint>();
                    var builder = new SocketBuilder(services.BuildServiceProvider());
                    builder.UseEndPoint<ImmediatelyCompleteEndPoint>();
                    var app = builder.Build();
                    var options = new HttpSocketOptions();
                    options.WebSockets.CloseTimeout = TimeSpan.FromSeconds(1);

                    var task = dispatcher.ExecuteAsync(context, options, app);

                    await task.OrTimeout();
                }
            });
        }

        private static DefaultHttpContext MakeRequest(string path, DefaultConnectionContext connection, string format = null)
        {
            var context = new DefaultHttpContext();
            context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
            context.Request.Path = path;
            context.Request.Method = "GET";
            var values = new Dictionary<string, StringValues>();
            values["id"] = connection.ConnectionId;
            if (format != null)
            {
                values["format"] = format;
            }
            var qs = new QueryCollection(values);
            context.Request.Query = qs;
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static void SetTransport(HttpContext context, TransportType transportType)
        {
            switch (transportType)
            {
                case TransportType.WebSockets:
                    context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketConnectionFeature());
                    break;
                case TransportType.ServerSentEvents:
                    context.Request.Headers["Accept"] = "text/event-stream";
                    break;
                default:
                    break;
            }
        }

        private static ConnectionManager CreateConnectionManager()
        {
            return new ConnectionManager(new Logger<ConnectionManager>(new LoggerFactory()), new EmptyApplicationLifetime());
        }
    }

    internal class TestWebSocketConnectionFeature : IHttpWebSocketFeature, IDisposable
    {
        public bool IsWebSocketRequest => true;

        public WebSocketChannel Client { get; private set; }

        public Task<WebSocket> AcceptAsync() => AcceptAsync(new WebSocketAcceptContext());

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
        {
            var clientToServer = Channel.CreateUnbounded<WebSocketMessage>();
            var serverToClient = Channel.CreateUnbounded<WebSocketMessage>();

            var clientSocket = new WebSocketChannel(serverToClient.Reader, clientToServer.Writer);
            var serverSocket = new WebSocketChannel(clientToServer.Reader, serverToClient.Writer);

            Client = clientSocket;
            return Task.FromResult<WebSocket>(serverSocket);
        }

        public void Dispose()
        {
        }

        public class WebSocketChannel : WebSocket
        {
            private readonly ChannelReader<WebSocketMessage> _input;
            private readonly ChannelWriter<WebSocketMessage> _output;

            private WebSocketCloseStatus? _closeStatus;
            private string _closeStatusDescription;
            private WebSocketState _state;

            public WebSocketChannel(ChannelReader<WebSocketMessage> input, ChannelWriter<WebSocketMessage> output)
            {
                _input = input;
                _output = output;
            }

            public override WebSocketCloseStatus? CloseStatus => _closeStatus;

            public override string CloseStatusDescription => _closeStatusDescription;

            public override WebSocketState State => _state;

            public override string SubProtocol => null;

            public override void Abort()
            {
                _output.TryComplete(new OperationCanceledException());
                _state = WebSocketState.Aborted;
            }

            public void SendAbort()
            {
                _output.TryComplete(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
            }

            public override async Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
            {
                await SendMessageAsync(new WebSocketMessage
                {
                    CloseStatus = closeStatus,
                    CloseStatusDescription = statusDescription,
                    MessageType = WebSocketMessageType.Close,
                },
                cancellationToken);

                _state = WebSocketState.CloseSent;

                _output.TryComplete();
            }

            public override async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
            {
                await SendMessageAsync(new WebSocketMessage
                {
                    CloseStatus = closeStatus,
                    CloseStatusDescription = statusDescription,
                    MessageType = WebSocketMessageType.Close,
                },
                cancellationToken);

                _state = WebSocketState.CloseSent;

                _output.TryComplete();
            }

            public override void Dispose()
            {
                _state = WebSocketState.Closed;
                _output.TryComplete();
            }

            public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                try
                {
                    await _input.WaitToReadAsync();

                    if (_input.TryRead(out var message))
                    {
                        if (message.MessageType == WebSocketMessageType.Close)
                        {
                            _state = WebSocketState.CloseReceived;
                            _closeStatus = message.CloseStatus;
                            _closeStatusDescription = message.CloseStatusDescription;
                            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, message.CloseStatus, message.CloseStatusDescription);
                        }

                        // REVIEW: This assumes the buffer passed in is > the buffer received
                        Buffer.BlockCopy(message.Buffer, 0, buffer.Array, buffer.Offset, message.Buffer.Length);

                        return new WebSocketReceiveResult(message.Buffer.Length, message.MessageType, message.EndOfMessage);
                    }
                }
                catch (WebSocketException ex)
                {
                    switch (ex.WebSocketErrorCode)
                    {
                        case WebSocketError.ConnectionClosedPrematurely:
                            _state = WebSocketState.Aborted;
                            break;
                    }

                    throw;
                }

                throw new InvalidOperationException("Unexpected close");
            }

            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            {
                var copy = new byte[buffer.Count];
                Buffer.BlockCopy(buffer.Array, buffer.Offset, copy, 0, buffer.Count);
                return SendMessageAsync(new WebSocketMessage
                {
                    Buffer = copy,
                    MessageType = messageType,
                    EndOfMessage = endOfMessage
                },
                cancellationToken);
            }

            public async Task<WebSocketConnectionSummary> ExecuteAndCaptureFramesAsync()
            {
                var frames = new List<WebSocketMessage>();
                while (await _input.WaitToReadAsync())
                {
                    while (_input.TryRead(out var message))
                    {
                        if (message.MessageType == WebSocketMessageType.Close)
                        {
                            _state = WebSocketState.CloseReceived;
                            _closeStatus = message.CloseStatus;
                            _closeStatusDescription = message.CloseStatusDescription;
                            return new WebSocketConnectionSummary(frames, new WebSocketReceiveResult(0, message.MessageType, message.EndOfMessage, message.CloseStatus, message.CloseStatusDescription));
                        }

                        frames.Add(message);
                    }
                }
                _state = WebSocketState.Closed;
                _closeStatus = WebSocketCloseStatus.InternalServerError;
                return new WebSocketConnectionSummary(frames, new WebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true, closeStatus: WebSocketCloseStatus.InternalServerError, closeStatusDescription: ""));
            }

            private async Task SendMessageAsync(WebSocketMessage webSocketMessage, CancellationToken cancellationToken)
            {
                while (await _output.WaitToWriteAsync(cancellationToken))
                {
                    if (_output.TryWrite(webSocketMessage))
                    {
                        break;
                    }
                }
            }
        }

        public class WebSocketConnectionSummary
        {
            public IList<WebSocketMessage> Received { get; }
            public WebSocketReceiveResult CloseResult { get; }

            public WebSocketConnectionSummary(IList<WebSocketMessage> received, WebSocketReceiveResult closeResult)
            {
                Received = received;
                CloseResult = closeResult;
            }
        }

        public class WebSocketMessage
        {
            public byte[] Buffer { get; set; }
            public WebSocketMessageType MessageType { get; set; }
            public bool EndOfMessage { get; set; }
            public WebSocketCloseStatus? CloseStatus { get; set; }
            public string CloseStatusDescription { get; set; }
        }
    }
}
