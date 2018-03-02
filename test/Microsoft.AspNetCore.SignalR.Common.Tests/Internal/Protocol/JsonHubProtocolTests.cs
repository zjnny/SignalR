// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    using System.Buffers;
    using System.IO.Pipelines;
    using System.Linq;
    using System.Threading.Tasks;
    using static HubMessageHelpers;

    public class JsonHubProtocolTests
    {
        private static readonly IDictionary<string, string> TestHeaders = new Dictionary<string, string>
        {
            { "Foo", "Bar" },
            { "KeyWith\nNew\r\nLines", "Still Works" },
            { "ValueWithNewLines", "Also\nWorks\r\nFine" },
        };

        // It's cleaner to do this as a prefix and use concatenation rather than string interpolation because JSON is already filled with '{'s.
        private static readonly string SerializedHeaders = "\"headers\":{\"Foo\":\"Bar\",\"KeyWith\\nNew\\r\\nLines\":\"Still Works\",\"ValueWithNewLines\":\"Also\\nWorks\\r\\nFine\"}";

        public static IDictionary<string, ProtocolTestData> TestData = TestDataItems.ToDictionary(d => d.Name);
        public static IEnumerable<object[]> TestDataNames = TestData.Keys.Select(k => new object[] { k });

        public static IEnumerable<ProtocolTestData> TestDataItems => new[]
        {
            new ProtocolTestData("InvocationWithIdAndArguments", new InvocationMessage("123", "Target", null, 1, "Foo", 2.0f), true, NullValueHandling.Ignore, "{\"type\":1,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}"),
            new ProtocolTestData("InvocationWithNoIdAndArguments", new InvocationMessage(null, "Target", null, 1, "Foo", 2.0f), true, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}"),
            new ProtocolTestData("InvocationWithSingleBoolArgument", new InvocationMessage(null, "Target", null, true), true, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[true]}"),
            new ProtocolTestData("InvocationWithSingleNullArgument", new InvocationMessage(null, "Target", null, new object[] { null }), true, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[null]}"),
            new ProtocolTestData("InvocationWithCustomObjectPascalCased", new InvocationMessage(null, "Target", null, new CustomObject()), false, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"ByteArrProp\":\"AQID\"}]}"),
            new ProtocolTestData("InvocationWithCustomObjectCamelCased", new InvocationMessage(null, "Target", null, new CustomObject()), true, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"byteArrProp\":\"AQID\"}]}"),
            new ProtocolTestData("InvocationWithCustomObjectPascalCasedNullIncluded", new InvocationMessage(null, "Target", null, new CustomObject()), false, NullValueHandling.Include, "{\"type\":1,\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null,\"ByteArrProp\":\"AQID\"}]}"),
            new ProtocolTestData("InvocationWithCustomObjectCamelCasedNullIncluded", new InvocationMessage(null, "Target", null, new CustomObject()), true, NullValueHandling.Include, "{\"type\":1,\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}]}"),
            new ProtocolTestData("InvocationWithHeaders", AddHeaders(TestHeaders, new InvocationMessage("123", "Target", null, 1, "Foo", 2.0f)), true, NullValueHandling.Ignore, "{\"type\":1," + SerializedHeaders + ",\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}"),

            new ProtocolTestData("StreamItemWithIntPayload", new StreamItemMessage("123", 1), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":1}"),
            new ProtocolTestData("StreamItemWithStringPayload", new StreamItemMessage("123", "Foo"), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":\"Foo\"}"),
            new ProtocolTestData("StreamItemWithFloatPayload", new StreamItemMessage("123", 2.0f), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":2.0}"),
            new ProtocolTestData("StreamItemWithBooleanPayload", new StreamItemMessage("123", true), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":true}"),
            new ProtocolTestData("StreamItemWithNullPayload", new StreamItemMessage("123", null), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":null}"),
            new ProtocolTestData("StreamItemWithCustomObjectPascalCased", new StreamItemMessage("123", new CustomObject()), false, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"ByteArrProp\":\"AQID\"}}"),
            new ProtocolTestData("StreamItemWithCustomObjectCamelCased", new StreamItemMessage("123", new CustomObject()), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"byteArrProp\":\"AQID\"}}"),
            new ProtocolTestData("StreamItemWithCustomObjectPascalCasedNullIncluded", new StreamItemMessage("123", new CustomObject()), false, NullValueHandling.Include, "{\"type\":2,\"invocationId\":\"123\",\"item\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null,\"ByteArrProp\":\"AQID\"}}"),
            new ProtocolTestData("StreamItemWithCustomObjectCamelCasedNullIncluded", new StreamItemMessage("123", new CustomObject()), true, NullValueHandling.Include, "{\"type\":2,\"invocationId\":\"123\",\"item\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}}"),
            new ProtocolTestData("StreamItemWithHeaders", AddHeaders(TestHeaders, new StreamItemMessage("123", new CustomObject())), true, NullValueHandling.Include, "{\"type\":2," + SerializedHeaders + ",\"invocationId\":\"123\",\"item\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}}"),

            new ProtocolTestData("CompletionWithIntPayload", CompletionMessage.WithResult("123", 1), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":1}"),
            new ProtocolTestData("CompletionWithStringPayload", CompletionMessage.WithResult("123", "Foo"), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":\"Foo\"}"),
            new ProtocolTestData("CompletionWithFloatPayload", CompletionMessage.WithResult("123", 2.0f), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":2.0}"),
            new ProtocolTestData("CompletionWithBooleanPayload", CompletionMessage.WithResult("123", true), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":true}"),
            new ProtocolTestData("CompletionWithNullPayload", CompletionMessage.WithResult("123", null), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":null}"),
            new ProtocolTestData("CompletionWithCustomObjectPascalCased", CompletionMessage.WithResult("123", new CustomObject()), false, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"ByteArrProp\":\"AQID\"}}"),
            new ProtocolTestData("CompletionWithCustomObjectCamelCased", CompletionMessage.WithResult("123", new CustomObject()), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"byteArrProp\":\"AQID\"}}"),
            new ProtocolTestData("CompletionWithCustomObjectPascalCasedNullIncluded", CompletionMessage.WithResult("123", new CustomObject()), false, NullValueHandling.Include, "{\"type\":3,\"invocationId\":\"123\",\"result\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null,\"ByteArrProp\":\"AQID\"}}"),
            new ProtocolTestData("CompletionWithCustomObjectCamelCasedNullIncluded", CompletionMessage.WithResult("123", new CustomObject()), true, NullValueHandling.Include, "{\"type\":3,\"invocationId\":\"123\",\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}}"),
            new ProtocolTestData("CompletionWithResultAndHeaders", AddHeaders(TestHeaders, CompletionMessage.WithResult("123", new CustomObject())), true, NullValueHandling.Include, "{\"type\":3," + SerializedHeaders + ",\"invocationId\":\"123\",\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}}"),
            new ProtocolTestData("CompletionWithError", CompletionMessage.WithError("123", "Whoops!"), false, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"error\":\"Whoops!\"}"),
            new ProtocolTestData("CompletionWithErrorAndHeaders", AddHeaders(TestHeaders, CompletionMessage.WithError("123", "Whoops!")), false, NullValueHandling.Ignore, "{\"type\":3," + SerializedHeaders + ",\"invocationId\":\"123\",\"error\":\"Whoops!\"}"),
            new ProtocolTestData("CompletionWithNoResult", CompletionMessage.Empty("123"), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\"}"),
            new ProtocolTestData("CompletionWithNoResultAndHeaders", AddHeaders(TestHeaders, CompletionMessage.Empty("123")), true, NullValueHandling.Ignore, "{\"type\":3," + SerializedHeaders + ",\"invocationId\":\"123\"}"),

            new ProtocolTestData("StreamInvocationWithIdAndArguments", new StreamInvocationMessage("123", "Target", null, 1, "Foo", 2.0f), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}"),
            new ProtocolTestData("StreamInvocationWithNoIdAndArguments", new StreamInvocationMessage("123", "Target", null, 1, "Foo", 2.0f), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}"),
            new ProtocolTestData("StreamInvocationWithBooleanArgument", new StreamInvocationMessage("123", "Target", null, true), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[true]}"),
            new ProtocolTestData("StreamInvocationWithNullArgument", new StreamInvocationMessage("123", "Target", null, new object[] { null }), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[null]}"),
            new ProtocolTestData("StreamInvocationWithCustomObjectPascalCased", new StreamInvocationMessage("123", "Target", null, new CustomObject()), false, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"ByteArrProp\":\"AQID\"}]}"),
            new ProtocolTestData("StreamInvocationWithCustomObjectCamelCased", new StreamInvocationMessage("123", "Target", null, new CustomObject()), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"byteArrProp\":\"AQID\"}]}"),
            new ProtocolTestData("StreamInvocationWithCustomObjectPascalCasedNullIncluded", new StreamInvocationMessage("123", "Target", null, new CustomObject()), false, NullValueHandling.Include, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null,\"ByteArrProp\":\"AQID\"}]}"),
            new ProtocolTestData("StreamInvocationWithCustomObjectCamelCasedNullIncluded", new StreamInvocationMessage("123", "Target", null, new CustomObject()), true, NullValueHandling.Include, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}]}"),
            new ProtocolTestData("StreamInvocationWithHeaders", AddHeaders(TestHeaders, new StreamInvocationMessage("123", "Target", null, new CustomObject())), true, NullValueHandling.Include, "{\"type\":4," + SerializedHeaders + ",\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}]}"),

            new ProtocolTestData("CancelInvocation", new CancelInvocationMessage("123"), true, NullValueHandling.Ignore, "{\"type\":5,\"invocationId\":\"123\"}"),
            new ProtocolTestData("CancelInvocationWithHeaders", AddHeaders(TestHeaders, new CancelInvocationMessage("123")), true, NullValueHandling.Ignore, "{\"type\":5," + SerializedHeaders + ",\"invocationId\":\"123\"}"),

            new ProtocolTestData("PingMessage", PingMessage.Instance, true, NullValueHandling.Ignore, "{\"type\":6}"),
        };

        [Theory]
        [MemberData(nameof(TestDataNames))]
        public async Task WriteMessage(string name)
        {
            var testData = TestData[name];

            var expectedOutput = testData.Json + (char)TextMessageFormat.RecordSeparator;

            var protocolOptions = new JsonHubProtocolOptions
            {
                PayloadSerializerSettings = new JsonSerializerSettings()
                {
                    NullValueHandling = testData.NullValueHandling,
                    ContractResolver = testData.CamelCase ? new CamelCasePropertyNamesContractResolver() : new DefaultContractResolver()
                }
            };

            var protocol = new JsonHubProtocol(Options.Create(protocolOptions));

            var pipe = new Pipe();
            protocol.WriteMessage(pipe.Writer, testData.Message);
            await pipe.Writer.FlushAsync();
            pipe.Writer.Complete();

            var json = Encoding.UTF8.GetString(await pipe.Reader.ReadAllAsync());
            Assert.Equal(expectedOutput, json);
        }

        [Theory]
        [MemberData(nameof(TestDataNames))]
        public void ParseMessage(string name)
        {
            var testData = TestData[name];
            var input = testData.Json + (char)TextMessageFormat.RecordSeparator;

            var protocolOptions = new JsonHubProtocolOptions
            {
                PayloadSerializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = testData.NullValueHandling,
                    ContractResolver = testData.CamelCase ? new CamelCasePropertyNamesContractResolver() : new DefaultContractResolver()
                }
            };

            var binder = new TestBinder(testData.Message);
            var protocol = new JsonHubProtocol(Options.Create(protocolOptions));
            var buffer = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(input));

            Assert.True(protocol.TryParseMessage(ref buffer, binder, out var message));
            Assert.Equal(testData.Message, message, TestHubMessageEqualityComparer.Instance);
        }

        [Theory]
        [InlineData("", "Error reading JSON.")]
        [InlineData("null", "Unexpected JSON Token Type 'Null'. Expected a JSON Object.")]
        [InlineData("42", "Unexpected JSON Token Type 'Integer'. Expected a JSON Object.")]
        [InlineData("'foo'", "Unexpected JSON Token Type 'String'. Expected a JSON Object.")]
        [InlineData("[42]", "Unexpected JSON Token Type 'Array'. Expected a JSON Object.")]
        [InlineData("{}", "Missing required property 'type'.")]

        [InlineData("{'type':1,'headers':{\"Foo\": 42},'target':'test',arguments:[]}", "Expected header 'Foo' to be of type String.")]
        [InlineData("{'type':1,'headers':{\"Foo\": true},'target':'test',arguments:[]}", "Expected header 'Foo' to be of type String.")]
        [InlineData("{'type':1,'headers':{\"Foo\": null},'target':'test',arguments:[]}", "Expected header 'Foo' to be of type String.")]
        [InlineData("{'type':1,'headers':{\"Foo\": []},'target':'test',arguments:[]}", "Expected header 'Foo' to be of type String.")]

        [InlineData("{'type':1}", "Missing required property 'target'.")]
        [InlineData("{'type':1,'invocationId':42}", "Expected 'invocationId' to be of type String.")]
        [InlineData("{'type':1,'invocationId':'42'}", "Missing required property 'target'.")]
        [InlineData("{'type':1,'invocationId':'42','target':42}", "Expected 'target' to be of type String.")]
        [InlineData("{'type':1,'invocationId':'42','target':'foo'}", "Missing required property 'arguments'.")]
        [InlineData("{'type':1,'invocationId':'42','target':'foo','arguments':{}}", "Expected 'arguments' to be of type Array.")]

        [InlineData("{'type':2}", "Missing required property 'invocationId'.")]
        [InlineData("{'type':2,'invocationId':42}", "Expected 'invocationId' to be of type String.")]
        [InlineData("{'type':2,'invocationId':'42'}", "Missing required property 'item'.")]

        [InlineData("{'type':3}", "Missing required property 'invocationId'.")]
        [InlineData("{'type':3,'invocationId':42}", "Expected 'invocationId' to be of type String.")]
        [InlineData("{'type':3,'invocationId':'42','error':[]}", "Expected 'error' to be of type String.")]

        [InlineData("{'type':4}", "Missing required property 'invocationId'.")]
        [InlineData("{'type':4,'invocationId':42}", "Expected 'invocationId' to be of type String.")]
        [InlineData("{'type':4,'invocationId':'42','target':42}", "Expected 'target' to be of type String.")]
        [InlineData("{'type':4,'invocationId':'42','target':'foo'}", "Missing required property 'arguments'.")]
        [InlineData("{'type':4,'invocationId':'42','target':'foo','arguments':{}}", "Expected 'arguments' to be of type Array.")]

        [InlineData("{'type':9}", "Unknown message type: 9")]
        [InlineData("{'type':'foo'}", "Expected 'type' to be of type Integer.")]

        [InlineData("{'type':3,'invocationId':'42','error':'foo','result':true}", "The 'error' and 'result' properties are mutually exclusive.")]
        public void InvalidMessages(string input, string expectedMessage)
        {
            input = input + (char)TextMessageFormat.RecordSeparator;

            var binder = new TestBinder(Array.Empty<Type>(), typeof(object));
            var protocol = new JsonHubProtocol();
            var messages = new List<HubMessage>();
            var buffer = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(input));
            var ex = Assert.Throws<InvalidDataException>(() => protocol.TryParseMessage(ref buffer, binder, out _));
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData("{'type':1,'invocationId':'42','target':'foo','arguments':[]}", "Invocation provides 0 argument(s) but target expects 2.")]
        [InlineData("{'type':1,'invocationId':'42','target':'foo','arguments':[ 'abc', 'xyz']}", "Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.")]
        [InlineData("{'type':4,'invocationId':'42','target':'foo','arguments':[]}", "Invocation provides 0 argument(s) but target expects 2.")]
        [InlineData("{'type':4,'invocationId':'42','target':'foo','arguments':[ 'abc', 'xyz']}", "Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.")]
        public void ArgumentBindingErrors(string input, string expectedMessage)
        {
            input = input + (char)TextMessageFormat.RecordSeparator;

            var binder = new TestBinder(paramTypes: new[] { typeof(int), typeof(string) }, returnType: typeof(bool));
            var protocol = new JsonHubProtocol();
            var messages = new List<HubMessage>();
            var buffer = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(input));
            protocol.TryParseMessage(ref buffer, binder, out var message);
            var ex = Assert.Throws<InvalidDataException>(() => ((HubMethodInvocationMessage)message).Arguments);
            Assert.Equal(expectedMessage, ex.Message);
        }

        public struct ProtocolTestData
        {
            public string Name { get; }
            public HubMessage Message { get; }
            public bool CamelCase { get; }
            public NullValueHandling NullValueHandling { get; }
            public string Json { get; }

            public ProtocolTestData(string name, HubMessage message, bool camelCase, NullValueHandling nullValueHandling, string json)
            {
                Name = name;
                Message = message;
                Json = json;
                CamelCase = camelCase;
                NullValueHandling = nullValueHandling;
            }
        }
    }
}
