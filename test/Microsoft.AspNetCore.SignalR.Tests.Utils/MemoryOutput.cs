using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public static class MemoryOutput
    {
        public static Task<byte[]> GetOutputAsArrayAsync(Action<IOutput> action)
        {
            return GetOutputAsArrayAsync((output) =>
            {
                action(output); return Task.CompletedTask;
            });
        }

        public static async Task<byte[]> GetOutputAsArrayAsync(Func<IOutput, Task> action)
        {
            var pipe = new Pipe();
            try
            {
                await action(pipe.Writer);
                await pipe.Writer.FlushAsync();
                pipe.Writer.Complete();

                return await pipe.Reader.ReadAllAsync();
            }
            finally
            {
                pipe.Reader.Complete();
                pipe.Writer.Complete();
            }
        }
    }
}
