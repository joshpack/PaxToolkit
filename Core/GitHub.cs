using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace pxdToolkit.Core;

public sealed record ReleaseAsset(string Name, string Url, long Size);

public sealed record Release(string Tag, string Url, DateTime Published, string Notes, IReadOnlyList<ReleaseAsset> Assets)
{
    public Version? Version => Versions.Parse(Tag);

    public ReleaseAsset? Find(string pattern) =>
        Assets.FirstOrDefault(a => Regex.IsMatch(a.Name, pattern, RegexOptions.IgnoreCase));

    /// <summary>The checksum file published next to an asset ("x.zip.sha256"), if there is one.</summary>
    public ReleaseAsset? ChecksumFor(ReleaseAsset asset) =>
        Assets.FirstOrDefault(a => a.Name.Equals(asset.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads releases through GitHub's public API (no sign-in, so public repos only).</summary>
public sealed class GitHub
{
    private readonly HttpClient _http;
    private readonly string _api;

    public GitHub(HttpClient http)
    {
        _http = http;
        _api = Environment.GetEnvironmentVariable("PXDTK_GITHUB_API") is { Length: > 0 } api ? api.TrimEnd('/') : "https://api.github.com";
    }

    public static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromMinutes(30),
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("pxdToolkit", Versions.Current.ToString(3)));
        return http;
    }

    /// <summary>
    /// The newest release; null when the repo has none. Answers are cached with their ETag: GitHub doesn't count a
    /// "not modified" reply against its hourly limit, and when it is limiting us anyway the saved answer is used.
    /// Throws on network trouble (with nothing saved) or rate limits (with nothing saved).
    /// </summary>
    public async Task<Release?> LatestAsync(string repo, CancellationToken ct = default)
    {
        var cacheFile = Path.Combine(Paths.Data, "releases", repo.Replace('/', '_') + ".json");
        string? etag = null, cached = null;
        try
        {
            if (File.Exists(cacheFile))
            {
                var lines = await File.ReadAllLinesAsync(cacheFile, ct);
                etag = lines.FirstOrDefault();
                cached = string.Join("\n", lines.Skip(1));
            }
        }
        catch
        {
            /* no cache */
        }

        HttpResponseMessage res;
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"{_api}/repos/{repo}/releases/latest"))
        {
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (etag is { Length: > 0 } && cached is { Length: > 0 })
                req.Headers.TryAddWithoutValidation("If-None-Match", etag);
            try
            {
                res = await _http.SendAsync(req, ct);
            }
            catch (HttpRequestException) when (cached is { Length: > 0 })
            {
                return ParseRelease(cached);   // offline: show what we knew
            }
        }

        using (res)
        {
            if (res.StatusCode == HttpStatusCode.NotModified && cached is { Length: > 0 })
                return ParseRelease(cached);
            if (res.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (res.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
                return cached is { Length: > 0 }
                    ? ParseRelease(cached)
                    : throw new HttpRequestException("GitHub is limiting requests from this PC. Try again in an hour.");
            res.EnsureSuccessStatusCode();

            var body = await res.Content.ReadAsStringAsync(ct);
            var release = ParseRelease(body);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                await File.WriteAllTextAsync(cacheFile, (res.Headers.ETag?.ToString() ?? "") + "\n" + body.Replace("\r", "").Replace("\n", " "), ct);
            }
            catch
            {
                /* caching is a bonus */
            }

            return release;
        }
    }

    private static Release ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list))
            foreach (var a in list.EnumerateArray())
                assets.Add(new ReleaseAsset(
                    a.GetProperty("name").GetString() ?? "",
                    a.GetProperty("browser_download_url").GetString() ?? "",
                    a.TryGetProperty("size", out var size) ? size.GetInt64() : 0));

        return new Release(
            root.GetProperty("tag_name").GetString() ?? "",
            root.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "",
            root.TryGetProperty("published_at", out var pub) && pub.ValueKind == JsonValueKind.String ? pub.GetDateTime() : DateTime.MinValue,
            root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "",
            assets);
    }
}

public static class Versions
{
    public static Version Current { get; } =
        typeof(Versions).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(1, 0, 0);

    /// <summary>"v1.3.8", "1.2.0+abc", "0.001-beta", "1.0.0.0" → a comparable 3-part version.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var m = Regex.Match(text, @"(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!m.Success)
            return null;
        int Part(int i) => m.Groups[i].Success && int.TryParse(m.Groups[i].Value, out var n) ? n : 0;
        return new Version(Part(1), Part(2), Part(3));
    }

    public static string Show(Version? v) => v is null ? "?" : v.ToString(3);
}
