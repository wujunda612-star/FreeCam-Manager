using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeCamManager.Core.Services;

public enum ManagerUpdateStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed class ManagerUpdateManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("displayVersion")] public string DisplayVersion { get; set; } = "";
    [JsonPropertyName("sourceRef")] public string SourceRef { get; set; } = "";
    [JsonPropertyName("sourceArchiveUrl")] public string SourceArchiveUrl { get; set; } = "";
    [JsonPropertyName("sourceSha256")] public string SourceSha256 { get; set; } = "";
    [JsonPropertyName("publishedAt")] public string PublishedAt { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
}

public sealed record ManagerUpdateCheckResult(
    ManagerUpdateStatus Status,
    Version CurrentVersion,
    Version? LatestVersion,
    DateTimeOffset CheckedAt,
    string Message,
    ManagerUpdateManifest? Manifest = null,
    string? Error = null)
{
    public bool UpdateAvailable => Status == ManagerUpdateStatus.UpdateAvailable && Manifest is not null;
}

public sealed record ManagerUpdateStageResult(
    string StagingDirectory,
    string SourceArchivePath,
    string SourceRoot,
    string UpdaterScriptPath,
    ManagerUpdateManifest Manifest);

public sealed class ManagerUpdateService : IDisposable
{
    private static readonly Uri ManifestUri = new("https://raw.githubusercontent.com/wujunda612-star/FreeCam-Manager/main/manager/update.json");
    private static readonly TimeSpan CheckCooldown = TimeSpan.FromSeconds(3);
    private const string TrustedRepositoryHost = "raw.githubusercontent.com";
    private const string TrustedArchivePrefix = "/wujunda612-star/FreeCam-Manager/";
    private const long MaxArchiveBytes = 64L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ManagerUpdateCheckResult? _lastCheckResult;
    private DateTimeOffset _lastCheckCompletedAt = DateTimeOffset.MinValue;

    public ManagerUpdateService(string updateRoot, HttpClient? httpClient = null)
    {
        UpdateRoot = updateRoot;
        if (httpClient is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
        }
    }

    public string UpdateRoot { get; }

    public async Task<ManagerUpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken ct = default)
    {
        currentVersion = AppVersionService.Normalize(currentVersion);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_lastCheckResult is not null &&
                _lastCheckResult.CurrentVersion == currentVersion &&
                now - _lastCheckCompletedAt < CheckCooldown)
            {
                return _lastCheckResult;
            }

            var result = await CheckCoreAsync(currentVersion, ct).ConfigureAwait(false);
            _lastCheckResult = result;
            _lastCheckCompletedAt = DateTimeOffset.UtcNow;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ManagerUpdateCheckResult> CheckCoreAsync(Version currentVersion, CancellationToken ct)
    {
        try
        {
            using var request = CreateManifestRequest();
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var manifest = await ParseManifestResponseAsync(response.Content, ct).ConfigureAwait(false);

            ValidateManifestIdentity(manifest);
            if (!AppVersionService.TryParse(manifest.Version, out var latestVersion))
                throw new InvalidDataException($"更新版本号无效：{manifest.Version}");

            var checkedAt = DateTimeOffset.Now;
            if (latestVersion <= currentVersion)
            {
                return new ManagerUpdateCheckResult(
                    ManagerUpdateStatus.UpToDate, currentVersion, latestVersion, checkedAt,
                    $"当前已是最新版本：{AppVersionService.FormatDisplay(currentVersion)}", manifest);
            }

            ValidateUpdatePackage(manifest);
            return new ManagerUpdateCheckResult(
                ManagerUpdateStatus.UpdateAvailable, currentVersion, latestVersion, checkedAt,
                $"发现新版本：{manifest.DisplayVersion}", manifest);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ManagerUpdateCheckResult(
                ManagerUpdateStatus.Failed, currentVersion, null, DateTimeOffset.Now,
                "软件更新检查失败，继续使用当前版本", null, ex.Message);
        }
    }

    public async Task<ManagerUpdateStageResult> StageAsync(ManagerUpdateManifest manifest, CancellationToken ct = default)
    {
        ValidateManifestIdentity(manifest);
        ValidateUpdatePackage(manifest);
        var safeVersion = SafeSegment(manifest.Version);
        var staging = Path.Combine(UpdateRoot, safeVersion);
        var archivePath = Path.Combine(staging, "source.zip");
        var extractRoot = Path.Combine(staging, "source");

        Directory.CreateDirectory(UpdateRoot);
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        try
        {
            using (var request = CreateNoCacheRequest(new Uri(manifest.SourceArchiveUrl, UriKind.Absolute)))
            using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength is > MaxArchiveBytes)
                    throw new InvalidDataException("更新源码包超过允许大小");

                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
                var buffer = new byte[64 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read <= 0) break;
                    total += read;
                    if (total > MaxArchiveBytes) throw new InvalidDataException("更新源码包超过允许大小");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
            }

            var actualSha = ComputeSha256(archivePath);
            if (!string.Equals(actualSha, manifest.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"更新源码包 SHA256 不匹配：expected={manifest.SourceSha256}, actual={actualSha}");

            Directory.CreateDirectory(extractRoot);
            ExtractZipSafely(archivePath, extractRoot);
            var sourceRoot = LocateSourceRoot(extractRoot);
            var updaterScript = Path.Combine(sourceRoot, "src-wpf", "Apply_Manager_Update.ps1");
            var buildScript = Path.Combine(sourceRoot, "src-wpf", "Build_v3_On_Windows.ps1");
            var solution = Path.Combine(sourceRoot, "src-wpf", "FreeCamManager.sln");
            if (!File.Exists(updaterScript) || !File.Exists(buildScript) || !File.Exists(solution))
                throw new InvalidDataException("更新源码包缺少预期的构建/更新入口");

            return new ManagerUpdateStageResult(staging, archivePath, sourceRoot, updaterScript, manifest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WriteStageFailure(staging, archivePath, manifest, ex);
            throw;
        }
    }

    private static void WriteStageFailure(string stagingDirectory, string archivePath, ManagerUpdateManifest manifest, Exception error)
    {
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            var timestamp = DateTimeOffset.Now;
            var text = string.Join(Environment.NewLine, new[]
            {
                "FreeCam Manager update staging failure",
                $"time: {timestamp:O}",
                $"version: {manifest.Version}",
                $"displayVersion: {manifest.DisplayVersion}",
                $"sourceArchiveUrl: {manifest.SourceArchiveUrl}",
                $"sourceArchivePath: {archivePath}",
                $"errorType: {error.GetType().FullName}",
                $"error: {error.Message}",
                "",
                error.ToString()
            });
            File.WriteAllText(Path.Combine(stagingDirectory, "UPDATE_STAGE_ERROR.txt"), text);

            var status = new
            {
                schemaVersion = 1,
                stage = "staging_failed",
                version = manifest.Version,
                displayVersion = manifest.DisplayVersion,
                timestamp = timestamp.ToString("O"),
                sourceArchiveUrl = manifest.SourceArchiveUrl,
                sourceArchivePath = archivePath,
                errorType = error.GetType().FullName,
                error = error.Message
            };
            File.WriteAllText(
                Path.Combine(stagingDirectory, "UPDATE_STATUS.json"),
                JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Diagnostics are best-effort and must never mask the original update failure.
        }
    }


    private static async Task<ManagerUpdateManifest> ParseManifestResponseAsync(HttpContent content, CancellationToken ct)
    {
        var payload = await content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload)) throw new InvalidDataException("更新清单为空");

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        // GitHub may honor the raw media type and return update.json directly.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schemaVersion", out _))
        {
            return JsonSerializer.Deserialize<ManagerUpdateManifest>(payload, JsonOptions)
                   ?? throw new InvalidDataException("更新清单为空");
        }

        // The Contents API normally returns a metadata envelope with base64 file content.
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("content", out var contentNode) &&
            root.TryGetProperty("encoding", out var encodingNode))
        {
            var encoded = contentNode.GetString() ?? "";
            var encoding = encodingNode.GetString() ?? "";
            if (!string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"GitHub 更新清单编码不受支持：{encoding}");

            try
            {
                var decoded = Convert.FromBase64String(encoded);
                var manifest = JsonSerializer.Deserialize<ManagerUpdateManifest>(decoded, JsonOptions);
                return manifest ?? throw new InvalidDataException("更新清单为空");
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("GitHub 更新清单 base64 内容无效", ex);
            }
        }

        throw new InvalidDataException("GitHub Contents API 未返回可识别的更新清单");
    }

    internal static void ValidateManifestIdentity(ManagerUpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1) throw new InvalidDataException($"不支持的更新清单版本：{manifest.SchemaVersion}");
        if (!AppVersionService.TryParse(manifest.Version, out _)) throw new InvalidDataException("更新版本号无效");
        if (string.IsNullOrWhiteSpace(manifest.DisplayVersion)) throw new InvalidDataException("更新显示版本为空");
    }

    internal static void ValidateUpdatePackage(ManagerUpdateManifest manifest)
    {
        if (!IsCommitSha(manifest.SourceRef)) throw new InvalidDataException("更新 sourceRef 必须是 40 位 Git commit SHA");
        if (!Uri.TryCreate(manifest.SourceArchiveUrl, UriKind.Absolute, out var archiveUri)) throw new InvalidDataException("更新源码地址无效");
        if (!string.Equals(archiveUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(archiveUri.Host, TrustedRepositoryHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新源码地址不在受信任的 GitHub 仓库");
        var expectedPrefix = $"{TrustedArchivePrefix}{manifest.SourceRef}/manager/packages/";
        if (!archiveUri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
            !archiveUri.AbsolutePath.EndsWith("_Source.zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新源码地址与 sourceRef 不匹配");
        if (!IsSha256(manifest.SourceSha256))
            throw new InvalidDataException("更新源码 SHA256 无效");
    }

    private static HttpRequestMessage CreateManifestRequest()
    {
        var request = CreateNoCacheRequest(ManifestUri);
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("FreeCam-Manager-Updater/1.0");
        return request;
    }

    private static HttpRequestMessage CreateNoCacheRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, WithCacheBust(uri));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        return request;
    }

    private static Uri WithCacheBust(Uri uri)
    {
        var builder = new UriBuilder(uri);
        var marker = $"_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}";
        var query = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrWhiteSpace(query) ? marker : $"{query}&{marker}";
        return builder.Uri;
    }

    private static void ExtractZipSafely(string archivePath, string destination)
    {
        var root = Path.GetFullPath(destination);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var normalizedName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(root, normalizedName));
            if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) && !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"更新源码包包含越界路径：{entry.FullName}");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string LocateSourceRoot(string extractRoot)
    {
        if (HasBuildEntrypoints(extractRoot)) return extractRoot;
        foreach (var dir in Directory.EnumerateDirectories(extractRoot))
            if (HasBuildEntrypoints(dir)) return dir;
        throw new InvalidDataException("更新源码包中找不到 src-wpf 构建入口");
    }

    private static bool HasBuildEntrypoints(string root)
        => File.Exists(Path.Combine(root, "src-wpf", "Build_v3_On_Windows.ps1"))
           && File.Exists(Path.Combine(root, "src-wpf", "Apply_Manager_Update.ps1"))
           && File.Exists(Path.Combine(root, "src-wpf", "FreeCamManager.sln"));

    private static bool IsCommitSha(string value)
        => value.Length == 40 && value.All(c => char.IsAsciiHexDigit(c));

    private static string ComputeSha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(c => char.IsAsciiHexDigit(c));

    private static string SafeSegment(string value)
    {
        var safe = new string(value.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "update" : safe;
    }

    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}
