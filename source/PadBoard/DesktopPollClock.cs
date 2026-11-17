using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace ControllerKeyboard;

internal sealed class DesktopPollClock : IDisposable
{
    private const int VisibleInterval = 4;
    private const int HiddenInterval = 8;
    private readonly Dispatcher dispatcher;
    private readonly Action poll;
    private readonly Action deliver;
    private readonly ManualResetEvent stop = new(false);
    private readonly WaitHandle timer;
    private readonly Thread worker;
    private int posted;
    private volatile bool disposed;

    private DesktopPollClock(SafeWaitHandle handle, Dispatcher dispatcher, Action poll)
    {
        this.dispatcher = dispatcher;
        this.poll = poll;
        deliver = Deliver;
        timer = new TimerHandle(handle);
        worker = new Thread(WaitForTick) { IsBackground = true, Name = "VitaPad controller timer" };
        worker.Start();
    }

    public static DesktopPollClock? TryStart(Dispatcher dispatcher, Action poll, bool visible)
    {
        const uint timerAccess = 0x00100002; // SYNCHRONIZE | TIMER_MODIFY_STATE
        SafeWaitHandle handle = CreateWaitableTimerEx(0, null, 2, timerAccess);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = CreateWaitableTimerEx(0, null, 0, timerAccess);
        }

        if (handle.IsInvalid || !SetInterval(handle, visible))
        {
            handle.Dispose();
            return null;
        }

        return new DesktopPollClock(handle, dispatcher, poll);
    }

    public void SetVisible(bool visible) => SetInterval(timer.SafeWaitHandle, visible);

    private static bool SetInterval(SafeWaitHandle handle, bool visible)
    {
        int period = visible ? VisibleInterval : HiddenInterval;
        long dueTime = -period * 10000L;
        return SetWaitableTimer(handle, ref dueTime, period, 0, 0, false);
    }

    private void WaitForTick()
    {
        WaitHandle[] handles = [stop, timer];
        while (WaitHandle.WaitAny(handles) == 1)
        {
            if (dispatcher.HasShutdownStarted)
                return;

            // Keep only one callback queued if the UI thread is busy.
            if (Interlocked.CompareExchange(ref posted, 1, 0) == 0)
                dispatcher.BeginInvoke(DispatcherPriority.Input, deliver);
        }
    }

    private void Deliver()
    {
        try
        {
            if (!disposed)
                poll();
        }
        finally
        {
            Volatile.Write(ref posted, 0);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        stop.Set();
        worker.Join();
        timer.Dispose();
        stop.Dispose();
    }

    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWaitableTimerExW", ExactSpelling = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll")]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, nint callback, nint argument, bool resume);
}
