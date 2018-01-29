using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets.Http.Internal
{
    // Borrowed from dotnet/corefx
    // These are the WebSocket overloads that take a Memory<T> (and friends). Copied over here to be usable from netstandard.
    // Since we are netstandard, we can't use the real overloads, which also means we can't use the optimized versions if a stream provides one
    // but this means we get the "best" version of the implementation for now (presuming the corefx version is "best" ;))
    // In order to prevent issues when there is a version of netstandard with these, I've also renamed them.
    // -anurse
    internal static class WebSocketMemoryExtensions
    {
        public static async ValueTask<WebSocketReceiveResult> ReceiveMemoryAsync(this WebSocket socket, Memory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (buffer.TryGetArray(out ArraySegment<byte> arraySegment))
            {
                WebSocketReceiveResult r = await socket.ReceiveAsync(arraySegment, cancellationToken).ConfigureAwait(false);
                return new WebSocketReceiveResult(r.Count, r.MessageType, r.EndOfMessage);
            }
            else
            {
                byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
                try
                {
                    WebSocketReceiveResult r = await socket.ReceiveAsync(new ArraySegment<byte>(array, 0, buffer.Length), cancellationToken).ConfigureAwait(false);
                    new Span<byte>(array, 0, r.Count).CopyTo(buffer.Span);
                    return new WebSocketReceiveResult(r.Count, r.MessageType, r.EndOfMessage);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(array);
                }
            }
        }
    }
}
