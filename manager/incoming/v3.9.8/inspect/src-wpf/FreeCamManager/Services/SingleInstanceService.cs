using System.Threading;

namespace FreeCamManager.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\FreeCamManager.SingleInstance";
    private const string ActivationEventName = @"Local\FreeCamManager.SingleInstance.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _registration;
    private bool _ownsMutex;

    public bool TryAcquirePrimary()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew) return false;

        _ownsMutex = true;
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        return true;
    }

    public void StartListening(Action onActivate)
    {
        if (!_ownsMutex || _activationEvent is null) throw new InvalidOperationException("Only the primary instance can listen for activation.");
        _registration ??= ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            static (state, timedOut) =>
            {
                if (!timedOut && state is Action action) action();
            },
            onActivate,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public static void SignalPrimaryInstance()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var activation = EventWaitHandle.OpenExisting(ActivationEventName);
                activation.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                if (attempt < 3) Thread.Sleep(75);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        if (_ownsMutex && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex?.Dispose();
        _mutex = null;
        _ownsMutex = false;
    }
}
