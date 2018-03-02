using System;
using System.Buffers;
using Microsoft.AspNetCore.SignalR.Internal;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal
{
    public class ReadOnlySequenceStreamTests
    {
        [Fact]
        public void CanRead()
        {
            var stream = new ReadOnlySequenceStream(
                new ReadOnlySequence<byte>(
                    new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));

            var buffer = new byte[10];
            var read = stream.Read(buffer, 3, 3);
            Assert.Equal(3, read);
            Assert.Equal(new byte[] { 0, 0, 0, 1, 2, 3, 0, 0, 0, 0 }, buffer);
        }

        [Fact]
        public void CanReadAcrossBlocks()
        {
            var (head, tail) = MemoryList<byte>.Create(
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
                new byte[] { 7, 8, 9 });

            var stream = new ReadOnlySequenceStream(
                new ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length));

            var buffer = new byte[9];
            var read = stream.Read(buffer, 0, 2);
            Assert.Equal(2, read);
            Assert.Equal(new byte[] { 1, 2, 0, 0, 0, 0, 0, 0, 0 }, buffer);

            read = stream.Read(buffer, 2, 7);
            Assert.Equal(7, read);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, buffer);
        }

        [Fact]
        public void ReadsTerminateAtEnd()
        {
            var stream = new ReadOnlySequenceStream(
                new ReadOnlySequence<byte>(
                    new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));

            var buffer = new byte[10];
            var read = stream.Read(buffer, 0, 9);
            Assert.Equal(9, read);

            read = stream.Read(buffer, 0, 9);
            Assert.Equal(1, read);
            Assert.Equal(new byte[] { 10, 2, 3, 4, 5, 6, 7, 8, 9, 0 }, buffer);
        }

        [Fact]
        public void ReadsZeroAtEnd()
        {
            var stream = new ReadOnlySequenceStream(
                new ReadOnlySequence<byte>(
                    new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));

            var buffer = new byte[10];
            var read = stream.Read(buffer, 0, 10);
            Assert.Equal(10, read);
            Assert.Equal(0, stream.Read(buffer, 0, 10));
        }

        private class MemoryList<T> : IMemoryList<T>
        {
            public Memory<T> Memory { get; }
            public IMemoryList<T> Next { get; private set; }
            public long RunningIndex { get; private set; }

            private MemoryList(Memory<T> memory)
            {
                Memory = memory;
            }

            public static (MemoryList<T> Head, MemoryList<T> tail) Create(params T[][] arrays)
            {
                MemoryList<T> head = null;
                MemoryList<T> tail = null;
                foreach (var array in arrays)
                {
                    var next = new MemoryList<T>(array);
                    if (head == null)
                    {
                        head = next;
                    }
                    else
                    {
                        tail.Next = next;
                        next.RunningIndex = tail.RunningIndex + tail.Memory.Length;
                    }

                    tail = next;
                }

                return (head, tail);
            }
        }
    }
}
