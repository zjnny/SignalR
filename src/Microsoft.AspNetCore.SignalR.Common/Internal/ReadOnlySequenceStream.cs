using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    public class ReadOnlySequenceStream : Stream
    {
        private ReadOnlySequence<byte> _sequence;

        // For implementing Stream.Position
        private long _offset = 0;

        public ReadOnlySequenceStream(ReadOnlySequence<byte> sequence)
        {
            _sequence = sequence;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _sequence.Length;

        public override long Position { get => _offset; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var target = buffer.AsSpan().Slice(offset, count);

            var readSize = (int)Math.Min(count, _sequence.Length);

            // Slice off the section to be read
            var toRead = _sequence.Slice(0, readSize);
            _sequence = _sequence.Slice(toRead.End);

            toRead.CopyTo(target);

            _offset += readSize;
            return readSize;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
