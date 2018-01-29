using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets.Http.Internal
{
    // Borrowed from dotnet/corefx
    // These are the Stream overloads that take a Memory<T> (and friends). Copied over here to be usable from netstandard.
    // Since we are netstandard, we can't use the real overloads, which also means we can't use the optimized versions if a stream provides one
    // but this means we get the "best" version of the implementation for now (presuming the corefx version is "best" ;))
    // In order to prevent issues when there is a version of netstandard with these, I've also renamed them.
    // -anurse
    internal static class StreamMemoryExtensions
    {
        public static Task WriteMemoryAsync(this Stream stream, ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (MemoryMarshal.TryGetArray(source, out ArraySegment<byte> array))
            {
                return stream.WriteAsync(array.Array, array.Offset, array.Count, cancellationToken);
            }
            else
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(source.Length);
                source.Span.CopyTo(buffer);
                return FinishWriteAsync(stream.WriteAsync(buffer, 0, source.Length, cancellationToken), buffer);

                async Task FinishWriteAsync(Task writeTask, byte[] localBuffer)
                {
                    try
                    {
                        await writeTask.ConfigureAwait(false);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(localBuffer);
                    }
                }
            }
        }
    }
}
