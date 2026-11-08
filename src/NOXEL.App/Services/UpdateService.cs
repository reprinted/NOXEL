using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.UI.Dispatching;
using Noxel.Core.Infrastructure;
using Noxel.Core.Settings;
using Noxel.Core.Updates;

namespace Noxel.App.Services;

public enum InstallKind { Installed, Portable }
public enum DownloadStage { None, Downloading, Verifying, Ready, Failed }

/// <summary>
/// GitHub-release based updates for NOXEL.
///
/// Source: the fixed public repository reprinted/NOXEL-Releases (GitHub REST "latest release", which never returns
/// drafts or prereleases). Checks: ~40 s after start, then every 24 h while running, plus on demand — one HTTPS
/// request each, nothing runs in between. Offline or GitHub errors are reported quietly.
///
/// A download is only accepted when ALL of these hold:
///  1. it comes from the release returned by the fixed API URL, over HTTPS, from github.com / *.githubusercontent.com
///     (also after redirects);
///  2. the release tag is a valid semantic version newer than this build;
///  3. its size equals the size GitHub reports;
///  4. its SHA-256 equals GitHub's own asset digest and/or the release's SHA256SUMS.txt (if both exist they must agree;
///     if neither exists NOXEL refuses to install and offers the manual download page);
///  5. it is a Windows executable whose embedded product version equals the release version;
///  6. if a pinned code-signing publisher is configured (<see cref="RequiredSigner"/>), a valid Authenticode signature
///     from that publisher.
/// Note: a checksum published in the same GitHub release proves integrity, not publisher identity — anyone who
/// controls the GitHub account controls both. Code signing (6) is the planned upgrade; no keys live in the app.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Set to the certificate subject (e.g. "CN=…") once NOXEL builds are code-signed. Null = not enforced.</summary>
    public static readonly string? RequiredSigner = null;

    private static readonly HttpClient Http = CreateClient();
    private readonly NoxelController _c;
    private readonly DispatcherQueue _ui;
    private DispatcherQueueTimer? _firstCheck, _daily;
    private CancellationTokenSource? _downloadCts;
    private bool _checking;

    public SemVer Current { get; }
    public InstallKind Kind { get; }
    public string AssetName => Kind == InstallKind.Installed ? UpdateSource.InstallerAsset : UpdateSource.PortableAsset;
    public ReleaseInfo? Latest { get; private set; }
    public UpdateCheckResult? LastResult { get; private set; }
    public bool Checking => _checking;

    public DownloadStage Stage { get; private set; }
    public long Received { get; private set; }
    public long Total { get; private set; }
    public double BytesPerSecond { get; private set; }
    public string StageMessage { get; private set; } = "";
    public string? ReadyPath { get; private set; }
    public string? ReadySha256 { get; private set; }
    public SemVer? ReadyVersion { get; private set; }

    public event Action? Changed;

    /// <summary>Local test override (any build): only loopback URLs are accepted, so it can never point at the internet.</summary>
    private static string ApiUrl => Environment.GetEnvironmentVariable("NOXEL_UPDATE_TEST_API") is { } t
        && Uri.TryCreate(t, UriKind.Absolute, out var u) && u.IsLoopback ? t : UpdateSource.LatestReleaseApi;
    private static bool TestMode => ApiUrl != UpdateSource.LatestReleaseApi;

    public UpdateService(NoxelController c)
    {
        _c = c;
        _ui = c.Ui;
        Current = CurrentVersion();
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? "";
        Kind = File.Exists(Path.Combine(exeDir, "unins000.exe")) ? InstallKind.Installed : InstallKind.Portable;
    }

    public static SemVer CurrentVersion()
    {
        var info = typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return SemVer.TryParse(info, out var v) ? v : new SemVer(0, 0, 0, "");
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var h = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NOXEL-Updater", CurrentVersion().ToString()));
        return h;
    }

    // ------------------------------------------------------------ scheduling

    public void Start()
    {
        _firstCheck = _ui.CreateTimer();
        _firstCheck.Interval = TestMode ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(40);
        _firstCheck.IsRepeating = false;
        _firstCheck.Tick += async (_, _) => { if (_c.Settings.UpdateMode != UpdateMode.Manual) await CheckAsync(manual: false); };
        _firstCheck.Start();
        _daily = _ui.CreateTimer();
        _daily.Interval = TimeSpan.FromHours(24);
        _daily.IsRepeating = true;
        _daily.Tick += async (_, _) => { if (_c.Settings.UpdateMode != UpdateMode.Manual) await CheckAsync(manual: false); };
        _daily.Start();
    }

    public void Stop()
    {
        _firstCheck?.Stop();
        _daily?.Stop();
        _downloadCts?.Cancel();
    }

    // ------------------------------------------------------------ check

    public async Task<UpdateCheckResult> CheckAsync(bool manual)
    {
        if (_checking) return LastResult ?? new UpdateCheckResult(UpdateCheckStatus.Error, null, "A check is already running.");
        _checking = true;
        Raise();
        UpdateCheckResult result;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var resp = await Http.SendAsync(req, cts.Token);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                result = UpdateDecision.Evaluate(Current, null);
            else if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                result = new(UpdateCheckStatus.Error, null, "GitHub is limiting requests right now. NOXEL will try again later.");
            else if (!resp.IsSuccessStatusCode)
                result = new(UpdateCheckStatus.Error, null, $"GitHub answered {(int)resp.StatusCode}. NOXEL will try again later.");
            else
            {
                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                var release = GitHubReleaseParser.Parse(json, out var err, allowLoopback: TestMode);
                result = release is null ? new(UpdateCheckStatus.Error, null, err) : UpdateDecision.Evaluate(Current, release);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            result = new(UpdateCheckStatus.Offline, null, "Couldn't reach GitHub. NOXEL works offline and will check again later.");
        }
        finally { _checking = false; }

        LastResult = result;
        if (result.Release is not null) Latest = result.Release;
        var s = _c.Settings;
        s.LastUpdateCheck = DateTimeOffset.Now;
        if (result.Release is not null) s.LatestKnownVersion = result.Release.Version.ToString();
        _c.SaveSoon();
        Log.Info($"Update check: {result.Status} {result.Message}");

        if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Release is { } r)
        {
            if (ReadyVersion is { } rv && rv.CompareTo(r.Version) == 0 && Stage == DownloadStage.Ready) NotifyReady();
            else if (s.UpdateMode == UpdateMode.AutoDownload && Stage != DownloadStage.Downloading) _ = DownloadAsync();
            else NotifyAvailable(r);
        }
        else if (manual)
        {
            _c.RaiseNotice(new Notice(result.Status == UpdateCheckStatus.UpToDate ? "You're up to date" : "Update check",
                result.Message, result.Status is UpdateCheckStatus.UpToDate or UpdateCheckStatus.NoReleases ? NoticeKind.Info : NoticeKind.Warning) { Key = "update" });
        }
        Raise();
        return result;
    }

    private void NotifyAvailable(ReleaseInfo r)
    {
        _c.RaiseNotice(new Notice($"NOXEL v{r.Version} available", "A new version of NOXEL is available.", NoticeKind.Info)
        {
            Key = "update",
            Persistent = true,
            Actions = new[]
            {
                new NoticeAction("Download", () => _ = DownloadAsync()),
                new NoticeAction("Later", () => { }),
                new NoticeAction("View changes", () => _c.RequestReleaseNotes(), Closes: false),
            },
        });
        if (App.IsMainWindowHidden) _c.Host?.ShowBalloon($"NOXEL v{r.Version} available", "Open NOXEL ▸ Settings ▸ Updates to download it.");
    }

    private void NotifyReady()
    {
        _c.RaiseNotice(new Notice($"NOXEL v{ReadyVersion} is ready to install", "Downloaded and verified. NOXEL will close, update and reopen.", NoticeKind.Success)
        {
            Key = "update",
            Persistent = true,
            Actions = new[]
            {
                new NoticeAction("Install now", () => _c.RequestInstallUpdate()),
                new NoticeAction("Later", () => { }),
            },
        });
    }

    // ------------------------------------------------------------ download + verification

    public async Task DownloadAsync()
    {
        var release = Latest;
        if (release is null || Stage == DownloadStage.Downloading) return;
        _c.RaiseNotice(new Notice($"Downloading NOXEL v{release.Version}", "Progress is shown in Settings › Updates. You can keep using NOXEL.", NoticeKind.Info) { Key = "update" });
        var asset = release.Asset(AssetName);
        if (asset is null) { Fail($"This release doesn't include {AssetName}. Download it manually from the releases page."); return; }
        if (asset.Size <= 0 || asset.Size > UpdateSource.MaxAssetBytes) { Fail("The release reports an invalid file size."); return; }

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;
        Stage = DownloadStage.Downloading;
        StageMessage = "Preparing download…";
        Received = 0; Total = asset.Size; BytesPerSecond = 0; ReadyPath = null;
        Raise();

        var dir = Path.Combine(AppPaths.Root, "updates", release.Version.ToString());
        var final = Path.Combine(dir, asset.Name);
        var partial = final + ".partial";
        try
        {
            var expected = await ExpectedHashAsync(release, asset, ct);
            if (expected is null)
            {
                Fail("This release has no published SHA-256 checksum, so NOXEL can't verify it and won't install it. Download it manually if you trust the source.");
                return;
            }
            Directory.CreateDirectory(dir);
            CleanOtherVersions(release.Version.ToString());

            using var req = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!UpdateSource.IsTrustedDownloadUri(resp.RequestMessage?.RequestUri, TestMode)) { Fail("The download was redirected to an unexpected host. Rejected."); return; }
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength is { } len && len != asset.Size) { Fail("The server reported a different file size than the release. Rejected."); return; }

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await resp.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
            {
                var buffer = new byte[1 << 16];
                var clock = Stopwatch.StartNew();
                long lastBytes = 0; var lastTick = TimeSpan.Zero;
                int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                {
                    if (Received + n > asset.Size) { Fail("The download is larger than the release says. Rejected."); return; }
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    sha.AppendData(buffer, 0, n);
                    Received += n;
                    if (clock.Elapsed - lastTick > TimeSpan.FromMilliseconds(250))
                    {
                        BytesPerSecond = (Received - lastBytes) / (clock.Elapsed - lastTick).TotalSeconds;
                        lastBytes = Received; lastTick = clock.Elapsed;
                        StageMessage = "Downloading";
                        Raise();
                    }
                }
            }

            Stage = DownloadStage.Verifying;
            StageMessage = "Verifying…";
            Raise();
            if (Received != asset.Size) { Delete(partial); Fail("The download was incomplete. Nothing was installed; try again."); return; }
            var actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            if (actual != expected) { Delete(partial); Fail("Checksum mismatch: the downloaded file doesn't match the release. It was deleted and will not run."); return; }
            var verdict = VerifyExecutable(partial, release.Version);
            if (verdict is not null) { Delete(partial); Fail(verdict); return; }

            File.Move(partial, final, overwrite: true);
            ReadyPath = final;
            ReadySha256 = actual;
            ReadyVersion = release.Version;
            Stage = DownloadStage.Ready;
            StageMessage = $"Verified (SHA-256 {actual[..12]}…). Ready to install.";
            Raise();
            NotifyReady();
        }
        catch (OperationCanceledException)
        {
            Delete(partial);
            Stage = DownloadStage.None;
            StageMessage = "Download cancelled.";
            Raise();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Delete(partial);
            Fail("The download failed: " + ex.Message.TrimEnd('.') + ". Try again later.");
        }
    }

    public void CancelDownload() => _downloadCts?.Cancel();

    private static async Task<string?> ExpectedHashAsync(ReleaseInfo release, ReleaseAsset asset, CancellationToken ct)
    {
        string? fromSums = null;
        var sums = release.Asset(UpdateSource.ChecksumAsset);
        if (sums is { Size: > 0 and < 65536 })
        {
            using var resp = await Http.GetAsync(sums.DownloadUrl, ct);
            if (resp.IsSuccessStatusCode && UpdateSource.IsTrustedDownloadUri(resp.RequestMessage?.RequestUri, TestMode))
                GitHubReleaseParser.ParseChecksums(await resp.Content.ReadAsStringAsync(ct)).TryGetValue(asset.Name, out fromSums);
        }
        if (asset.Sha256 is { } digest && fromSums is { } listed && digest != listed) return null; // disagreeing sources: refuse
        return asset.Sha256 ?? fromSums;
    }

    /// <summary>The file must be a PE executable whose product version is exactly the release version.</summary>
    private static string? VerifyExecutable(string path, SemVer expected)
    {
        using (var fs = File.OpenRead(path))
        {
            if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') return "The download isn't a Windows program. Rejected.";
        }
        var info = FileVersionInfo.GetVersionInfo(path);
        if (!SemVer.TryParse(info.ProductVersion, out var v) || v.CompareTo(expected) != 0)
            return $"The file says it is version {info.ProductVersion ?? "unknown"}, not v{expected}. Rejected.";
        if (RequiredSigner is { } signer && !Authenticode.IsSignedBy(path, signer))
            return "The file isn't signed by NOXEL's publisher. Rejected.";
        return null;
    }

    private void Fail(string message)
    {
        Stage = DownloadStage.Failed;
        StageMessage = message;
        Log.Warn("Update: " + message);
        Raise();
        _c.RaiseNotice(new Notice("Update not installed", message, NoticeKind.Error) { Key = "update" });
    }

    private static void CleanOtherVersions(string keep)
    {
        try
        {
            var root = Path.Combine(AppPaths.Root, "updates");
            foreach (var d in Directory.GetDirectories(root))
                if (!string.Equals(Path.GetFileName(d), keep, StringComparison.OrdinalIgnoreCase)) Directory.Delete(d, recursive: true);
        }
        catch { }
    }

    private static void Delete(string path) { try { File.Delete(path); } catch { } }

    // ------------------------------------------------------------ install

    /// <summary>Null when installing is safe right now, otherwise the reason it isn't.</summary>
    public string? InstallBlocker()
    {
        if (Stage != DownloadStage.Ready || ReadyPath is null || !File.Exists(ReadyPath)) return "Download and verify the update first.";
        if (_c.Changes?.Pending is not null) return "Finish the display Keep/Revert confirmation first. Updates never run during a display change.";
        return null;
    }

    /// <summary>Re-verifies the file, saves state and hands over to the installer / portable updater. NOXEL then exits.</summary>
    public string? Install()
    {
        var blocker = InstallBlocker();
        if (blocker is not null) return blocker;
        var path = ReadyPath!;
        using (var fs = File.OpenRead(path))
        {
            var again = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            if (again != ReadySha256) { Delete(path); Stage = DownloadStage.Failed; Raise(); return "The downloaded file changed after verification. It was deleted."; }
        }
        _c.SaveNow();
        try
        {
            if (Kind == InstallKind.Installed)
            {
                var log = Path.Combine(AppPaths.Root, "updates", "install.log");
                Process.Start(new ProcessStartInfo(path, $"/SILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /RELAUNCH=1 /LOG=\"{log}\"") { UseShellExecute = true });
            }
            else
            {
                var target = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
                Process.Start(new ProcessStartInfo(path, $"--apply-portable-update --pid {Environment.ProcessId} --target \"{target}\"") { UseShellExecute = false });
            }
        }
        catch (Exception ex)
        {
            return "Couldn't start the update: " + ex.Message;
        }
        Log.Info($"Handing over to {Path.GetFileName(path)} v{ReadyVersion}");
        App.Current.ExitApp();
        return null;
    }

    private void Raise() => _ui.TryEnqueue(() => Changed?.Invoke());
}

/// <summary>WinVerifyTrust-based Authenticode check (used once builds are code-signed).</summary>
internal static class Authenticode
{
    public static bool IsSignedBy(string path, string subject)
    {
        if (!IsTrusted(path)) return false;
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return string.Equals(cert.Subject, subject, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsTrusted(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /*none*/, fdwRevocationChecks = 0,
                dwUnionChoice = 1 /*file*/, pFile = pFile, dwStateAction = 0, dwProvFlags = 0x10 /*safer*/,
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        finally { Marshal.FreeHGlobal(pFile); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO { public uint cbStruct; [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice;
        public uint fdwRevocationChecks; public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction;
        public IntPtr hWVTStateData; public IntPtr pwszURLReference; public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] ref Guid action, ref WINTRUST_DATA data);
}
