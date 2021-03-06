// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.Http.Connections.Client.Internal
{
    public partial class ServerSentEventsTransport : ITransport
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        // Volatile so that the SSE loop sees the updated value set from a different thread
        private volatile Exception _error;
        private readonly CancellationTokenSource _transportCts = new CancellationTokenSource();
        private readonly ServerSentEventsMessageParser _parser = new ServerSentEventsMessageParser();

        private IDuplexPipe _application;

        public Task Running { get; private set; } = Task.CompletedTask;

        public ServerSentEventsTransport(HttpClient httpClient)
            : this(httpClient, null)
        { }

        public ServerSentEventsTransport(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException(nameof(_httpClient));
            }

            _httpClient = httpClient;
            _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ServerSentEventsTransport>();
        }

        public Task StartAsync(Uri url, IDuplexPipe application, TransferFormat transferFormat, IConnection connection)
        {
            if (transferFormat != TransferFormat.Text)
            {
                throw new ArgumentException($"The '{transferFormat}' transfer format is not supported by this transport.", nameof(transferFormat));
            }

            _application = application;

            Log.StartTransport(_logger, transferFormat);

            var startTcs = new TaskCompletionSource<object>(TaskContinuationOptions.RunContinuationsAsynchronously);

            Running = ProcessAsync(url, startTcs);

            return startTcs.Task;
        }

        private async Task ProcessAsync(Uri url, TaskCompletionSource<object> startTcs)
        {
            // Start sending and polling (ask for binary if the server supports it)
            var receiving = OpenConnection(_application, url, startTcs, _transportCts.Token);
            var sending = SendUtils.SendMessages(url, _application, _httpClient, _logger);

            // Wait for send or receive to complete
            var trigger = await Task.WhenAny(receiving, sending);

            if (trigger == receiving)
            {
                // We're waiting for the application to finish and there are 2 things it could be doing
                // 1. Waiting for application data
                // 2. Waiting for an outgoing send (this should be instantaneous)

                // Cancel the application so that ReadAsync yields
                _application.Input.CancelPendingRead();
            }
            else
            {
                // Set the sending error so we communicate that to the application
                _error = sending.IsFaulted ? sending.Exception.InnerException : null;

                _transportCts.Cancel();

                // Cancel any pending flush so that we can quit
                _application.Output.CancelPendingFlush();
            }
        }

        private async Task OpenConnection(IDuplexPipe application, Uri url, TaskCompletionSource<object> startTcs, CancellationToken cancellationToken)
        {
            Log.StartReceive(_logger);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            HttpResponseMessage response = null;

            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                startTcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                response?.Dispose();
                Log.TransportStopping(_logger);
                startTcs.TrySetException(ex);
                return;
            }

            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                var pipeOptions = new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0);
                var pipelineReader = StreamPipeConnection.CreateReader(pipeOptions, stream);
                var readCancellationRegistration = cancellationToken.Register(
                    reader => ((PipeReader)reader).CancelPendingRead(), pipelineReader);
                try
                {
                    while (true)
                    {
                        var result = await pipelineReader.ReadAsync();
                        var input = result.Buffer;
                        if (result.IsCanceled || (input.IsEmpty && result.IsCompleted))
                        {
                            Log.EventStreamEnded(_logger);
                            break;
                        }

                        var consumed = input.Start;
                        var examined = input.End;
                        try
                        {
                            Log.ParsingSSE(_logger, input.Length);
                            var parseResult = _parser.ParseMessage(input, out consumed, out examined, out var buffer);
                            FlushResult flushResult = default;

                            switch (parseResult)
                            {
                                case ServerSentEventsMessageParser.ParseResult.Completed:
                                    Log.MessageToApp(_logger, buffer.Length);

                                    flushResult = await _application.Output.WriteAsync(buffer);

                                    _parser.Reset();
                                    break;
                                case ServerSentEventsMessageParser.ParseResult.Incomplete:
                                    if (result.IsCompleted)
                                    {
                                        throw new FormatException("Incomplete message.");
                                    }
                                    break;
                            }

                            // We canceled in the middle of applying back pressure
                            // or if the consumer is done
                            if (flushResult.IsCanceled || flushResult.IsCompleted)
                            {
                                break;
                            }
                        }
                        finally
                        {
                            pipelineReader.AdvanceTo(consumed, examined);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.ReceiveCanceled(_logger);
                }
                catch (Exception ex)
                {
                    _error = ex;
                }
                finally
                {
                    _application.Output.Complete(_error);

                    readCancellationRegistration.Dispose();

                    Log.ReceiveStopped(_logger);
                }
            }
        }

        public async Task StopAsync()
        {
            Log.TransportStopping(_logger);

            _application.Input.CancelPendingRead();

            try
            {
                await Running;
            }
            catch (Exception ex)
            {
                Log.TransportStopped(_logger, ex);
                throw;
            }

            Log.TransportStopped(_logger, null);
        }
    }
}
