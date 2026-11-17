using System.Runtime.InteropServices;

namespace VitaPad;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private RegisteredWaitHandle? listener;
    private bool ownsMutex;
    private volatile bool disposed;

    public SingleInstance(string name)
    {
        // Create the event before claiming the mutex so a relaunch during startup is retained.
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        mutex = new Mutex(false, name);
    }

    public bool TryAcquire()
    {
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (ownsMutex)
            return true;

        AllowSetForegroundWindow(uint.MaxValue);
        activation.Set();
        return false;
    }

    public void Listen(Action activate)
    {
        listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
        {
            if (!disposed)
                activate();
        }, null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        disposed = true;
        listener?.Unregister(null);
        activation.Dispose();
        if (ownsMutex)
            mutex.ReleaseMutex();
        mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
