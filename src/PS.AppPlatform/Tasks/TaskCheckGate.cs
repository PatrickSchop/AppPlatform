namespace PS.AppPlatform.Tasks;

/// <summary>
/// Process-wide gate serialising CheckAndStartTasksAsync. Singleton for the same reason
/// as ExecutionManagerIdentity: a per-scope semaphore serialises nothing.
/// </summary>
public sealed class TaskCheckGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Gate(this);
    }

    private void Release()
    {
        _semaphore.Release();
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }

    private sealed class Gate : IDisposable
    {
        private readonly TaskCheckGate _parent;
        private int _released = 0;

        public Gate(TaskCheckGate parent)
        {
            _parent = parent;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _parent.Release();
            }
        }
    }
}

