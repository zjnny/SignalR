using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    /// <summary>
    /// Provides an implementation of <see cref="IOutput"/> over pooled buffers
    /// </summary>
    public class MemoryOutput : IOutput
    {
        private readonly ArrayPool<byte> _pool;

        private MemorySegment _head;
        private MemorySegment _tail;
        private int _tailEnd;

        public MemoryOutput() : this(ArrayPool<byte>.Shared)
        {
            _head = null;
            _tail = null;
            _tailEnd = 0;
        }

        public MemoryOutput(ArrayPool<byte> pool)
        {
            _pool = pool;
        }

        public void Advance(int bytes)
        {
            var amountToAdvance = bytes;
        }

        public Memory<byte> GetMemory(int minimumLength = 0)
        {
            if (_tail == null || _tail.Data.Length - _tailEnd < minimumLength)
            {
                AddSegment(minimumLength);
            }

            Debug.Assert(_tail != null && _tail.Data.Length - _tailEnd > minimumLength);
            return _tail.Data.Slice(_tailEnd);
        }

        public Span<byte> GetSpan(int minimumLength = 0) => GetMemory(minimumLength).Span;

        private void AddSegment(int minimumLength)
        {
            // Create a new segment
            var newSegment = new MemorySegment()
            {
                Data = new _pool.Rent(minimumLength)
            };

            // Slice the data segment of the current tail if needed
            if (_tail != null)
            {
                if (_tailEnd < _tail.Data.Length)
                {
                    _tail.Data = _tail.Data.Slice(0, _tailEnd);
                }
            }
        }

        private class MemorySegment
        {
            public OwnedMemory<byte> Data;
            public MemorySegment Next;
        }
    }
}
