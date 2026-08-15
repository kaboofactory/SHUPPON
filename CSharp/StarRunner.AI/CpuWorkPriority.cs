using System.Threading;

using StarRunner.Core;

namespace StarRunner.AI;

/// <summary>
/// Optional BelowNormal scope for hosts that want background-friendly CPU work.
/// The AI library does not change priority unless explicitly requested; priority is restored on exit.
/// </summary>
internal static class CpuWorkPriority
{
    public static IDisposable Enter(bool useBelowNormal)
    {
        if (!useBelowNormal) return NoopScope.Instance;
        Thread thread = Thread.CurrentThread;
        ThreadPriority previous;

        try
        {
            previous = thread.Priority;
            if ((int)previous > (int)ThreadPriority.BelowNormal)
            {
                thread.Priority = ThreadPriority.BelowNormal;
            }
        }
        catch
        {
            return NoopScope.Instance;
        }

        return new PriorityScope(thread, previous);
    }

    private sealed class PriorityScope : IDisposable
    {
        private Thread? _thread;
        private readonly ThreadPriority _previous;

        public PriorityScope(Thread thread, ThreadPriority previous)
        {
            _thread = thread;
            _previous = previous;
        }

        public void Dispose()
        {
            Thread? thread = Interlocked.Exchange(ref _thread, null);
            if (thread is null)
            {
                return;
            }

            try
            {
                thread.Priority = _previous;
            }
            catch
            {
                // Priority is an optimization only; never fail a search because of it.
            }
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
