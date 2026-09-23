using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace FreeCamManager.Services;

public sealed class StartupTimingRecorder
{
    private sealed record Entry(string Name, double ElapsedMs, double DeltaMs, DateTimeOffset At, string Detail);

    private readonly Stopwatch _clock;
    private readonly string _version;
    private readonly string _sessionId = Guid.NewGuid().ToString("N")[..12];
    private readonly List<Entry> _entries = [];
    private readonly object _sync = new();
    private double _lastElapsedMs;
    private int _flushedCount;
    private bool _headerWritten;

    public StartupTimingRecorder(Stopwatch clock, string dataDirectory, string version)
    {
        _clock = clock;
        _version = version;
        FilePath = Path.Combine(dataDirectory, "Logs", "STARTUP_TIMING.log");
    }

    public string FilePath { get; }

    public void Mark(string name, params (string Key, object? Value)[] fields)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var elapsedMs = _clock.Elapsed.TotalMilliseconds;
        var deltaMs = elapsedMs - _lastElapsedMs;
        _lastElapsedMs = elapsedMs;
        var detail = fields.Length == 0
            ? ""
            : string.Join(";", fields.Select(x => $"{x.Key}={Sanitize(x.Value)}"));
        lock (_sync)
            _entries.Add(new Entry(name.Trim(), elapsedMs, deltaMs, DateTimeOffset.Now, detail));
    }

    public void Flush(string reason)
    {
        try
        {
            List<Entry> pending;
            lock (_sync)
            {
                if (_flushedCount >= _entries.Count) return;
                pending = _entries.Skip(_flushedCount).ToList();
                _flushedCount = _entries.Count;
            }

            var parent = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            var sb = new StringBuilder();
            if (!_headerWritten)
            {
                sb.AppendLine();
                sb.AppendLine($"=== STARTUP_SESSION session={_sessionId} version={_version} started={DateTimeOffset.Now:O} ===");
                _headerWritten = true;
            }
            foreach (var entry in pending)
            {
                sb.Append(entry.At.ToString("O", CultureInfo.InvariantCulture));
                sb.Append(" | mark=").Append(entry.Name);
                sb.Append(" | elapsed_ms=").Append(entry.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(" | delta_ms=").Append(entry.DeltaMs.ToString("F1", CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(entry.Detail)) sb.Append(" | ").Append(entry.Detail);
                sb.AppendLine();
            }
            sb.AppendLine($"--- FLUSH reason={Sanitize(reason)} ---");
            File.AppendAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // Diagnostics must never prevent Manager startup.
        }
    }

    private static string Sanitize(object? value) => (value?.ToString() ?? "")
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal)
        .Replace("|", "/", StringComparison.Ordinal);
}
