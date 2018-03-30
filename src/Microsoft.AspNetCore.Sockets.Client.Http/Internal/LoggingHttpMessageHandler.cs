// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.Sockets.Client.Http.Internal
{
    public class LoggingHttpMessageHandler : DelegatingHandler
    {
        private readonly ILogger<LoggingHttpMessageHandler> _logger;

        public LoggingHttpMessageHandler(HttpMessageHandler inner, ILoggerFactory loggerFactory) : base(inner)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _logger = loggerFactory.CreateLogger<LoggingHttpMessageHandler>();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Log.SendingHttpRequest(_logger, request.Method, request.RequestUri);

            var response = await base.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Log.UnsuccessfulHttpResponse(_logger, request.Method, request.RequestUri, response.StatusCode);
            }

            return response;
        }

        private static class Log
        {
            private static readonly Action<ILogger, HttpMethod, Uri, Exception> _sendingHttpRequest =
                LoggerMessage.Define<HttpMethod, Uri>(LogLevel.Trace, new EventId(1, "SendingHttpRequest"), "Sending {Method} request to '{RequestUrl}'.");

            private static readonly Action<ILogger, HttpMethod, Uri, HttpStatusCode, Exception> _unsuccessfulHttpResponse =
                LoggerMessage.Define<HttpMethod, Uri, HttpStatusCode>(LogLevel.Warning, new EventId(2, "UnsuccessfulHttpResponse"), "Unsuccessful HTTP response status code of {StatusCode} return from {Method} '{RequestUrl}'.");

            public static void SendingHttpRequest(ILogger logger, HttpMethod method, Uri requestUrl)
            {
                _sendingHttpRequest(logger, method, requestUrl, null);
            }
            public static void UnsuccessfulHttpResponse(ILogger logger, HttpMethod method, Uri requestUrl, HttpStatusCode statusCode)
            {
                _unsuccessfulHttpResponse(logger, method, requestUrl, statusCode, null);
            }
        }
    }
}
