namespace FreeCamManager.Core.Services;

public sealed class RuntimeLogController : IAppLogger, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly string _version;
    private readonly string _fallbackDirectory;
    private LogService? _current;
    private string _preferredDirectory = "";

    public RuntimeLogController(string version, string fallbackDirectory)
    {
        _version = version;
        _fallbackDirectory = fallbackDirectory;
    }

    public bool IsEnabled
    {
        get { lock (_gate) return _current is not null; }
    }

    public string UsedDirectory { get; private set; } = "";

    public bool Configure(bool enabled, string preferredDirectory, out string usedDirectory)
    {
        usedDirectory = "";
        if (!enabled)
        {
            LogService? old;
            lock (_gate)
            {
                old = _current;
                _current = null;
                _preferredDirectory = preferredDirectory ?? "";
                UsedDirectory = "";
            }
            DisposeBlocking(old);
            return true;
        }

        var preferred = preferredDirectory?.Trim() ?? "";
        lock (_gate)
        {
            if (_current is not null && string.Equals(_preferredDirectory, preferred, StringComparison.OrdinalIgnoreCase))
            {
                usedDirectory = UsedDirectory;
                return true;
            }
        }

        var next = LogService.TryCreateWithFallback(preferred, _version, out var actual, _fallbackDirectory);
        if (next is null) return false;

        LogService? previous;
        lock (_gate)
        {
            previous = _current;
            _current = next;
            _preferredDirectory = preferred;
            UsedDirectory = actual;
            usedDirectory = actual;
        }
        DisposeBlocking(previous);
        return true;
    }

    public void Event(string eventName, params (string Key, object? Value)[] fields)
    {
        LogService? current;
        lock (_gate) current = _current;
        current?.Event(eventName, fields);
    }

    public async ValueTask DisposeAsync()
    {
        LogService? current;
        lock (_gate)
        {
            current = _current;
            _current = null;
            UsedDirectory = "";
        }
        if (current is not null) await current.DisposeAsync().ConfigureAwait(false);
    }

    private static void DisposeBlocking(LogService? log)
    {
        if (log is null) return;
        try { log.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
    }
}
