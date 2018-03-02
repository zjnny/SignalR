using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Tests;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class MessageParserBenchmark
    {
        private static readonly Random Random = new Random();
        private byte[] _binaryInput;
        private byte[] _textInput;

        [Params(32, 64)]
        public int ChunkSize { get; set; }

        [Params(64, 128)]
        public int MessageLength { get; set; }

        [IterationSetup]
        public async Task Setup()
        {
            var buffer = new byte[MessageLength];
            Random.NextBytes(buffer);
            _binaryInput = await MemoryOutput.GetOutputAsArrayAsync(output =>
            {
                BinaryMessageFormat.WriteLengthPrefix(buffer.Length, output);
                output.Write(buffer);
            });

            buffer = new byte[MessageLength];
            Random.NextBytes(buffer);
            _textInput = await MemoryOutput.GetOutputAsArrayAsync(output =>
            {
                output.Write(buffer);
                TextMessageFormat.WriteRecordSeparator(output);
            });
        }

        [Benchmark]
        public void SingleBinaryMessage()
        {
            var buffer = new ReadOnlyBuffer<byte>(_binaryInput);
            if (!BinaryMessageFormat.TrySliceMessage(ref buffer, out _))
            {
                throw new InvalidOperationException("Failed to parse");
            }
        }

        [Benchmark]
        public void SingleTextMessage()
        {
            var buffer = new ReadOnlyBuffer<byte>(_textInput);
            if (!BinaryMessageFormat.TrySliceMessage(ref buffer, out _))
            {
                throw new InvalidOperationException("Failed to parse");
            }
        }
    }
}
