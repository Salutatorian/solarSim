using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolarSim.Preview.Updates;

/// <summary>
/// Checks / downloads updates from the official GitHub Releases only.
/// Never uploads project data. All files stay under %LocalAppData%\solarSim\updates\.
/// </summary>
internal sealed class AppUpdateService
{
    public const string Owner = "Salutatorian";
    public const string Repo = "solarSim";
    private const string ApiLatest = "https://api.github.com/repos/Salutatorian/solarSim/releases/latest";
    private const string ReleaseDownloadPathPrefix = "/Salutatorian/solarSim/releases/download/";

    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static AppUpdateService Instance { get; } = new();

    private readonly object _gate = new();
    private CancellationTokenSource? _downloadCts;

    public UpdateInfo? Available { get; private set; }
    public bool IsDownloading { get; private set; }
    public double DownloadProgress01 { get; private set; }
    public bool DownloadProgressIndeterminate { get; private set; }
    public string? DownloadError { get; private set; }
    public bool DownloadComplete { get; private set; }
    public bool UserDismissedToast { get; set; }
    public bool ApplyOnExit { get; set; } = true;

    public event Action? StateChanged;

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("solarSim", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public static string UpdatesRoot()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "solarSim",
            "updates");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string PendingMarkerPath() => Path.Combine(UpdatesRoot(), "pending.json");

    public bool HasStagedUpdate()
    {
        var pending = ReadPending();
        return pending is not null && File.Exists(pending.ZipPath);
    }

    public async Task CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiLatest);
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, ct)
                .ConfigureAwait(false);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return;

            var remote = NormalizeVersion(release.TagName);
            var local = NormalizeVersion(currentVersion);
            if (!IsNewer(remote, local))
            {
                lock (_gate)
                {
                    Available = null;
                    DownloadComplete = false;
                    DownloadProgress01 = 0;
                    DownloadProgressIndeterminate = false;
                    DownloadError = null;
                }
                Raise();
                return;
            }

            var asset = release.Assets?.FirstOrDefault(a =>
                a.Name is not null
                && a.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)
                && IsAllowedAssetUrl(a.BrowserDownloadUrl));

            if (asset?.BrowserDownloadUrl is null)
                return;

            var info = new UpdateInfo
            {
                Version = remote,
                TagName = release.TagName!,
                Notes = release.Body?.Trim() ?? "",
                ZipUrl = asset.BrowserDownloadUrl,
                ZipName = asset.Name ?? $"solarSim-{remote}-win-x64.zip",
                PublishedAt = release.PublishedAt,
            };

            lock (_gate)
            {
                Available = info;
                if (DownloadComplete && File.Exists(StagedZipPath(info.Version)))
                {
                    DownloadProgress01 = 1;
                    DownloadProgressIndeterminate = false;
                }
                else if (!IsDownloading)
                {
                    DownloadComplete = File.Exists(StagedZipPath(info.Version));
                    DownloadProgress01 = DownloadComplete ? 1 : 0;
                    DownloadProgressIndeterminate = false;
                    DownloadError = null;
                }
            }

            Raise();
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            DownloadError = ex.Message;
            Raise();
        }
    }

    public async Task StartDownloadAsync()
    {
        UpdateInfo? info;
        lock (_gate)
        {
            info = Available;
            if (info is null || IsDownloading) return;
            if (DownloadComplete && File.Exists(StagedZipPath(info.Version))) return;
            IsDownloading = true;
            DownloadProgress01 = 0;
            DownloadProgressIndeterminate = false;
            DownloadError = null;
            DownloadComplete = false;
            UserDismissedToast = false;
            _downloadCts?.Cancel();
            _downloadCts = new CancellationTokenSource();
        }
        Raise();

        var ct = _downloadCts!.Token;
        try
        {
            if (!IsAllowedAssetUrl(info!.ZipUrl))
                throw new InvalidOperationException("Update URL is not from the official GitHub Releases host.");

            var versionDir = Path.Combine(UpdatesRoot(), info.Version);
            Directory.CreateDirectory(versionDir);
            var zipPath = StagedZipPath(info.Version);

            using var req = new HttpRequestMessage(HttpMethod.Get, info.ZipUrl);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            if (resp.RequestMessage?.RequestUri is Uri final
                && !IsAllowedAssetUrl(final.ToString()))
                throw new InvalidOperationException("Update download redirected off the official host.");

            var total = resp.Content.Headers.ContentLength ?? -1L;
            DownloadProgressIndeterminate = total <= 0;
            await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var tmp = zipPath + ".partial";
            await using (var output = File.Create(tmp))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                           .ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    readTotal += read;
                    if (total > 0)
                    {
                        DownloadProgress01 = Math.Clamp(readTotal / (double)total, 0, 1);
                        DownloadProgressIndeterminate = false;
                    }
                    else
                    {
                        // Unknown size — creep toward ~92% so the bar isn't stuck at 0.
                        DownloadProgress01 = Math.Min(0.92, readTotal / (readTotal + 2_500_000.0));
                        DownloadProgressIndeterminate = true;
                    }
                    Raise();
                }
            }

            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(tmp, zipPath);

            var marker = new PendingUpdateMarker
            {
                Version = info.Version,
                ZipPath = zipPath,
                Notes = info.Notes,
                CheckedUtc = DateTime.UtcNow,
            };
            File.WriteAllText(PendingMarkerPath(), JsonSerializer.Serialize(marker, JsonOptions));

            lock (_gate)
            {
                IsDownloading = false;
                DownloadComplete = true;
                DownloadProgress01 = 1;
                DownloadProgressIndeterminate = false;
            }
            Raise();
        }
        catch (OperationCanceledException)
        {
            lock (_gate) { IsDownloading = false; }
            Raise();
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                IsDownloading = false;
                DownloadError = ex.Message;
            }
            Raise();
        }
    }

    public void CancelDownload()
    {
        _downloadCts?.Cancel();
        lock (_gate) { IsDownloading = false; }
        Raise();
    }

    public PendingUpdateMarker? ReadPending()
    {
        try
        {
            var path = PendingMarkerPath();
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PendingUpdateMarker>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public bool TryLaunchApplyAndExit(int currentProcessId)
    {
        var pending = ReadPending();
        if (pending is null || !File.Exists(pending.ZipPath)) return false;

        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var extractDir = Path.Combine(UpdatesRoot(), pending.Version, "extracted");
        var script = Path.Combine(UpdatesRoot(), "apply-update.cmd");
        var ps1 = Path.Combine(UpdatesRoot(), "expand-update.ps1");
        var exeName = "solarSim.exe";
        var notesPath = Path.Combine(UpdatesRoot(), "whats-new.txt");
        var pendingPath = PendingMarkerPath();
        File.WriteAllText(notesPath, pending.Notes ?? "");

        // Paths via env vars avoid quoting bugs in Expand-Archive.
        File.WriteAllText(ps1, """
$ErrorActionPreference = 'Stop'
Expand-Archive -LiteralPath $env:SOLARSIM_ZIP -DestinationPath $env:SOLARSIM_EXTRACT -Force
""");

        var cmd = $"""
@echo off
setlocal
set "PID={currentProcessId}"
set "SOLARSIM_ZIP={pending.ZipPath}"
set "SOLARSIM_EXTRACT={extractDir}"
set "APPDIR={appDir}"
set "EXE={exeName}"
set "NOTES={notesPath}"
set "PENDING={pendingPath}"
set "PS1={ps1}"
:wait
tasklist /FI "PID eq %PID%" 2>NUL | find "%PID%" >NUL
if not errorlevel 1 (
  timeout /t 1 /nobreak >NUL
  goto wait
)
if exist "%SOLARSIM_EXTRACT%" rmdir /s /q "%SOLARSIM_EXTRACT%"
mkdir "%SOLARSIM_EXTRACT%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
if errorlevel 1 exit /b 1
robocopy "%SOLARSIM_EXTRACT%" "%APPDIR%" /E /R:2 /W:1 /NFL /NDL /NJH /NJS >NUL
if errorlevel 8 exit /b 1
copy /Y "%NOTES%" "%APPDIR%\whats-new.txt" >NUL
if exist "%PENDING%" del /f /q "%PENDING%"
start "" "%APPDIR%\%EXE%"
del /f /q "%PS1%" >NUL 2>&1
del "%~f0"
""";
        File.WriteAllText(script, cmd);

        Process.Start(new ProcessStartInfo
        {
            FileName = script,
            UseShellExecute = true,
            WorkingDirectory = UpdatesRoot(),
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        return true;
    }

    public static string? ConsumeWhatsNewNotes()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "whats-new.txt");
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            File.Delete(path);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    public static string StagedZipPath(string version) =>
        Path.Combine(UpdatesRoot(), version, $"solarSim-{version}-win-x64.zip");

    public static bool IsAllowedAssetUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        var host = uri.Host;
        if (host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        return uri.AbsolutePath.StartsWith(ReleaseDownloadPathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeVersion(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('v') || raw.StartsWith('V'))
            raw = raw[1..];
        var plus = raw.IndexOf('+');
        if (plus > 0) raw = raw[..plus];
        var dash = raw.IndexOf('-');
        if (dash > 0) raw = raw[..dash];
        return raw;
    }

    public static bool IsNewer(string remote, string local)
    {
        if (!Version.TryParse(Pad(remote), out var r)) return false;
        if (!Version.TryParse(Pad(local), out var l)) return false;
        return r > l;
    }

    private static string Pad(string v)
    {
        var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var major = parts.Length > 0 ? parts[0] : "0";
        var minor = parts.Length > 1 ? parts[1] : "0";
        var build = parts.Length > 2 ? parts[2] : "0";
        var rev = parts.Length > 3 ? parts[3] : "0";
        return $"{major}.{minor}.{build}.{rev}";
    }

    private void Raise() => StateChanged?.Invoke();
}

internal sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public string TagName { get; set; } = "";
    public string Notes { get; set; } = "";
    public string ZipUrl { get; set; } = "";
    public string ZipName { get; set; } = "";
    public DateTimeOffset? PublishedAt { get; set; }
}

internal sealed class PendingUpdateMarker
{
    public string Version { get; set; } = "";
    public string ZipPath { get; set; } = "";
    public string? Notes { get; set; }
    public DateTime CheckedUtc { get; set; }
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}
