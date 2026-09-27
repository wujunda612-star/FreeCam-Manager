using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FreeCamManager.Core.Services;

public sealed class FilenameAliasService
{
    public sealed class FilenameTerm
    {
        [JsonPropertyName("from")] public string From { get; set; } = "";
        [JsonPropertyName("to")] public string To { get; set; } = "";
    }

    private sealed record CompiledTerm(Regex Pattern, string Replacement);

    private static readonly FilenameTerm[] DefaultTerms =
    [
        new() { From = "ExternalVerify", To = "外部验证" },
        new() { From = "Environment", To = "环境" },
        new() { From = "Experiment", To = "实验" },
        new() { From = "Runtime", To = "运行时" },
        new() { From = "Feature", To = "功能" },
        new() { From = "Bundle", To = "整合包" },
        new() { From = "Result", To = "结果" },
        new() { From = "Source", To = "源码" },
        new() { From = "Stable", To = "稳定版" },
        new() { From = "Probe", To = "探针" },
        new() { From = "Test", To = "测试" },
        new() { From = "Fix", To = "修复" },
        new() { From = "Env", To = "环境" }
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private IReadOnlyList<CompiledTerm> _compiled;

    public FilenameAliasService(string filePath)
    {
        FilePath = filePath;
        _compiled = Compile(DefaultTerms);
    }

    public string FilePath { get; }
    public int Count { get { lock (_gate) return _compiled.Count; } }

    public async Task EnsureFileAsync(CancellationToken ct = default)
    {
        if (File.Exists(FilePath)) return;
        var parent = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        var tmp = FilePath + ".tmp";
        await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024, true))
        {
            await JsonSerializer.SerializeAsync(stream, DefaultTerms, JsonOptions, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        File.Move(tmp, FilePath, true);
    }

    public async Task<int> EnsureAndReloadAsync(CancellationToken ct = default)
    {
        await EnsureFileAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ReloadAsync(CancellationToken ct = default)
    {
        await EnsureFileAsync(ct).ConfigureAwait(false);
        await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 32 * 1024, true);
        var terms = await JsonSerializer.DeserializeAsync<List<FilenameTerm>>(stream, JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("术语表内容为空。请输入 JSON 术语数组。");
        var compiled = Compile(terms);
        if (compiled.Count == 0) throw new InvalidDataException("术语表没有可用条目。每项都需要 from / to。");
        lock (_gate) _compiled = compiled;
        return compiled.Count;
    }

    public string Translate(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return filename;
        IReadOnlyList<CompiledTerm> terms;
        lock (_gate) terms = _compiled;
        var result = filename;
        foreach (var term in terms)
            result = term.Pattern.Replace(result, _ => term.Replacement);
        return result;
    }

    private static IReadOnlyList<CompiledTerm> Compile(IEnumerable<FilenameTerm> terms)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<CompiledTerm>();
        foreach (var term in terms
                     .Where(x => !string.IsNullOrWhiteSpace(x.From) && !string.IsNullOrWhiteSpace(x.To))
                     .OrderByDescending(x => x.From.Trim().Length))
        {
            var from = term.From.Trim();
            if (!seen.Add(from)) continue;
            var to = term.To.Trim();
            // Treat letters as part of the same English token, but allow digits to follow
            // terms such as Probe1.3 / Test12.1 / Fix2.
            var pattern = $@"(?<![A-Za-z]){Regex.Escape(from)}(?![A-Za-z])";
            list.Add(new CompiledTerm(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled), to));
        }
        return list;
    }
}
