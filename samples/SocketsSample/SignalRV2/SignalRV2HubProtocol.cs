// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SocketsSample
{
    public class SignalRV2HubProtocol : IHubProtocol
    {
        public JsonSerializer Serializer { get; } = new JsonSerializer();
        public string Name => "signalrv2";

        public TransferFormat TransferFormat => TransferFormat.Text;

        private string _hubName;

        public SignalRV2HubProtocol(string hubName)
        {
            _hubName = hubName;
        }

        public bool TryParseMessages(ReadOnlyMemory<byte> input, IInvocationBinder binder, IList<HubMessage> messages)
        {
            // REVIEW: This is a hack, we should implement a TextReader over the
            // pipe reader
            // PS: It's so easy to write bad .NET codez :D

            var obj = JObject.Parse(Encoding.UTF8.GetString(input.ToArray()));

            var hubName = obj.Value<string>("H");
            var invocationId = obj.Value<string>("I");
            var method = obj.Value<string>("M");
            var args = obj.Value<JArray>("A");

            ExceptionDispatchInfo argumentBindingException = null;
            object[] arguments = null;

            try
            {
                arguments = BindArguments(args, binder.GetParameterTypes(method));
            }
            catch (Exception ex)
            {
                argumentBindingException = ExceptionDispatchInfo.Capture(ex);
            }

            messages.Add(new InvocationMessage(invocationId, method, argumentBindingException, arguments));
            return true;
        }

        public void WriteMessage(HubMessage message, Stream output)
        {
            if (message is InvocationMessage im)
            {
                var bytes = GetJsonBytes(new
                {
                    M = new[] {
                           new {
                            A = im.Arguments,
                            M = im.Target,
                            H = _hubName
                           }
                        }
                });
                output.Write(bytes, 0, bytes.Length);
            }

            if (message is CompletionMessage cm)
            {
                var bytes = GetJsonBytes(new
                {
                    I = cm.InvocationId,
                    R = cm.Result,
                    E = cm.Error
                });
                output.Write(bytes, 0, bytes.Length);
            }

            // TODO: Handle unsupported messages types
        }

        private byte[] GetJsonBytes(object o)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o));
        }

        private object[] BindArguments(JArray args, IReadOnlyList<Type> paramTypes)
        {
            var arguments = new object[args.Count];
            if (paramTypes.Count != arguments.Length)
            {
                throw new InvalidDataException($"Invocation provides {arguments.Length} argument(s) but target expects {paramTypes.Count}.");
            }

            try
            {
                for (var i = 0; i < paramTypes.Count; i++)
                {
                    var paramType = paramTypes[i];
                    arguments[i] = args[i].ToObject(paramType, Serializer);
                }

                return arguments;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.", ex);
            }
        }
    }
}
