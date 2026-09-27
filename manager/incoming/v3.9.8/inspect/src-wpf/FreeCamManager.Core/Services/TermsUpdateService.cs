using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeCamManager.Core.Services;

public enum TermsUpdateStatus
{
    Local,
    UpToDate,
    Updated,
    Failed
}

public sealed record TermsLocalState(string Version, int TermCount);

public sealed record TermsUpdateResult(
    TermsUpdateStatus Status,
    string Version,
    int TermCount,
    DateTimeOffset CheckedAt,
    string Message,
    string? Error = null)
{
    public bool Updated => Status == TermsUpdateStatus.Updated;
}

public sealed class TermsUpdateService : IDisposable
{
    public sealed class TermsVersionInfo
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";
        [JsonPropertyName("termCount")] public int TermCount { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("termsFile")] public string TermsFile { get; set; } = "FilenameTerms.json";
    }

    private static readonly Uri VersionUri = new("https://raw.githubusercontent.com/wujunda612-star/FreeCam-Manager/main/version.json");
    private static readonly Uri TermsUri = new("https://raw.githubusercontent.com/wujunda612-star/FreeCam-Manager/main/FilenameTerms.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TermsUpdateService(string termsFilePath, string versionFilePath, HttpClient? httpClient = null)
    {
        TermsFilePath = termsFilePath;
        VersionFilePath = versionFilePath;
        if (httpClient is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
        }
    }

    public string TermsFilePath { get; }
    public string VersionFilePath { get; }

    public async Task<TermsLocalState> ReadLocalStateAsync(int fallbackTermCount, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(VersionFilePath)) return new TermsLocalState("内置", fallbackTermCount);
            await using var stream = new FileStream(VersionFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 16 * 1024, true);
            var info = await JsonSerializer.DeserializeAsync<TermsVersionInfo>(stream, JsonOptions, ct).ConfigureAwait(false);
            if (info is null || string.IsNullOrWhiteSpace(info.Version)) return new TermsLocalState("内置", fallbackTermCount);
            return new TermsLocalState(info.Version.Trim(), info.TermCount > 0 ? info.TermCount : fallbackTermCount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new TermsLocalState("内置", fallbackTermCount);
        }
    }

    public async Task<TermsUpdateResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var checkedAt = DateTimeOffset.Now;
            try
            {
                var versionJson = await GetFreshStringAsync(VersionUri, ct).ConfigureAwait(false);
                var remote = JsonSerializer.Deserialize<TermsVersionInfo>(versionJson, JsonOptions)
                    ?? throw new InvalidDataException("在线 version.json 内容为空。");
                ValidateVersion(remote);

                var currentHash = await ComputeFileSha256Async(TermsFilePath, ct).ConfigureAwait(false);
                if (string.Equals(currentHash, remote.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteVersionAtomicallyAsync(versionJson, ct).ConfigureAwait(false);
                    return new TermsUpdateResult(TermsUpdateStatus.UpToDate, remote.Version, remote.TermCount, checkedAt, "术语表已是最新版本");
                }

                var bytes = await GetFreshBytesAsync(TermsUri, ct).ConfigureAwait(false);
                var downloadedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(downloadedHash, remote.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"术语表 SHA256 校验失败：expected={remote.Sha256}, actual={downloadedHash}");

                var terms = JsonSerializer.Deserialize<List<FilenameAliasService.FilenameTerm>>(bytes, JsonOptions)
                    ?? throw new InvalidDataException("下载的术语表内容为空。");
                var validTerms = terms.Count(x => !string.IsNullOrWhiteSpace(x.From) && !string.IsNullOrWhiteSpace(x.To));
                if (validTerms == 0) throw new InvalidDataException("下载的术语表没有有效 from / to 条目。");
                if (remote.TermCount > 0 && validTerms != remote.TermCount)
                    throw new InvalidDataException($"术语数量不匹配：version.json={remote.TermCount}, 文件={validTerms}");

                await WriteTermsAtomicallyAsync(bytes, ct).ConfigureAwait(false);
                await WriteVersionAtomicallyAsync(versionJson, ct).ConfigureAwait(false);
                return new TermsUpdateResult(TermsUpdateStatus.Updated, remote.Version, validTerms, checkedAt, $"术语表已更新到 {remote.Version}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                return new TermsUpdateResult(TermsUpdateStatus.Failed, "未知", 0, checkedAt, "在线术语检查失败，继续使用本地术语表", ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> GetFreshStringAsync(Uri uri, CancellationToken ct)
    {
        using var request = CreateFreshRequest(uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<byte[]> GetFreshBytesAsync(Uri uri, CancellationToken ct)
    {
        using var request = CreateFreshRequest(uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateFreshRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BuildFreshUri(uri));
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache, no-store, max-age=0");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        return request;
    }

    private static Uri BuildFreshUri(Uri uri)
    {
        var builder = new UriBuilder(uri);
        var cacheBust = "_=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N");
        builder.Query = string.IsNullOrWhiteSpace(builder.Query)
            ? cacheBust
            : builder.Query.TrimStart('?') + "&" + cacheBust;
        return builder.Uri;
    }

    private static void ValidateVersion(TermsVersionInfo remote)
    {
        if (remote.SchemaVersion != 1) throw new InvalidDataException($"不支持的术语表 schemaVersion：{remote.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(remote.Version)) throw new InvalidDataException("version.json 缺少 version。");
        if (string.IsNullOrWhiteSpace(remote.Sha256) || remote.Sha256.Length != 64) throw new InvalidDataException("version.json 的 sha256 无效。");
        if (!string.Equals(remote.TermsFile, "FilenameTerms.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("version.json 的 termsFile 不是 FilenameTerms.json。");
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return "";
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 32 * 1024, true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task WriteTermsAtomicallyAsync(byte[] bytes, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(TermsFilePath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        var tmp = TermsFilePath + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
        File.Move(tmp, TermsFilePath, true);
    }

    private async Task WriteVersionAtomicallyAsync(string json, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(VersionFilePath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        var tmp = VersionFilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, json.Trim() + Environment.NewLine, Encoding.UTF8, ct).ConfigureAwait(false);
        File.Move(tmp, VersionFilePath, true);
    }

    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}
