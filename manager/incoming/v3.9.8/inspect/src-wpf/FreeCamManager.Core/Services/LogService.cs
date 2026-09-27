using System.Threading.Channels;

namespace FreeCamManager.Core.Services;

public sealed class LogService : IAppLogger, IAsyncDisposable
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(4096)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private readonly string _path;

    private LogService(string path)
    {
        _path = path;
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public string Path => _path;

    public static LogService Create(string directory, string version, int keep = 20)
    {
        Directory.CreateDirectory(directory);
        var name = $"FreeCamManager_{DateTime.Now:yyyyMMdd_HHmmss.fff}.log";
        var path = System.IO.Path.Combine(directory, name);
        // Probe synchronously so access errors are reported here instead of faulting the writer task later.
        using (var probe = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { }
        var service = new LogService(path);
        service.Event("SESSION_START", ("version", version), ("pid", Environment.ProcessId), ("log", service._path));
        service.Cleanup(directory, Math.Max(1, keep));
        return service;
    }

    public static LogService? TryCreateWithFallback(string preferredDirectory, string version, out string usedDirectory, string? fallbackDirectory = null, int keep = 20)
    {
        usedDirectory = "";
        var candidates = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var normalized = value.Trim();
            if (!candidates.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase))) candidates.Add(normalized);
        }

        Add(preferredDirectory);
        Add(fallbackDirectory);
        Add(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreeCamManager", "Logs"));

        foreach (var directory in candidates)
        {
            try
            {
                var service = Create(directory, version, keep);
                usedDirectory = directory;
                if (!string.Equals(directory, preferredDirectory?.Trim(), StringComparison.OrdinalIgnoreCase))
                    service.Event("LOG_FALLBACK", ("preferred", preferredDirectory), ("using", directory));
                return service;
            }
            catch
            {
                // Try the next candidate. Startup must not be blocked by an optional/custom log path.
            }
        }
        return null;
    }

    public void Event(string eventName, params (string Key, object? Value)[] fields)
    {
        var parts = new List<string> { $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}", $"event={eventName.Trim()}" };
        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            parts.Add($"{key}={Quote(value)}");
        }
        _channel.Writer.TryWrite(string.Join(' ', parts) + Environment.NewLine);
    }

    public async ValueTask DisposeAsync()
    {
        Event("SESSION_END");
        _channel.Writer.TryComplete();
        await _writerTask.ConfigureAwait(false);
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task WriterLoopAsync()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        await using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, true);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };
        await foreach (var line in _channel.Reader.ReadAllAsync()) await writer.WriteAsync(line);
    }

    private void Cleanup(string directory, int keep)
    {
        try
        {
            var files = new DirectoryInfo(directory).EnumerateFiles("*.log")
                .OrderByDescending(x => x.LastWriteTimeUtc).ToList();
            foreach (var file in files.Skip(keep))
                if (!string.Equals(file.FullName, _path, StringComparison.OrdinalIgnoreCase)) file.Delete();
        }
        catch { }
    }

    private static string Quote(object? value)
    {
        if (value is null) return "null";
        if (value is string s) return '"' + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
}
