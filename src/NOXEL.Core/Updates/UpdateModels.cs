// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using System.Globalization;
using System.Text.Json;

namespace Noxel.Core.Updates;

/// <summary>Semantic version (MAJOR.MINOR.PATCH[-prerelease][+build]); a leading "v" is accepted.</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch, string Prerelease) : IComparable<SemVer>
{
    public bool IsPrerelease => Prerelease.Length > 0;
    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";

    public static bool TryParse(string? text, out SemVer v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];                 // build metadata never affects ordering
        string pre = "";
        int dash = s.IndexOf('-');
        if (dash >= 0) { pre = s[(dash + 1)..]; s = s[..dash]; if (pre.Length == 0) return false; }
        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var a) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var b) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var c)) return false;
        if (a > 9999 || b > 9999 || c > 99999) return false;
        v = new SemVer(a, b, c, pre);
        return true;
    }

    public int CompareTo(SemVer o)
    {
        int r = Major.CompareTo(o.Major); if (r != 0) return r;
        r = Minor.CompareTo(o.Minor); if (r != 0) return r;
        r = Patch.CompareTo(o.Patch); if (r != 0) return r;
        if (!IsPrerelease && o.IsPrerelease) return 1;   // 1.0.0 > 1.0.0-rc.1
        if (IsPrerelease && !o.IsPrerelease) return -1;
        return string.CompareOrdinal(Prerelease, o.Prerelease);
    }

    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
}

public sealed record ReleaseAsset(string Name, long Size, Uri DownloadUrl, string? Sha256);

public sealed record ReleaseInfo(
    SemVer Version, string Tag, string Title, string Notes, DateTimeOffset? PublishedAt, Uri? PageUrl,
    bool Draft, bool Prerelease, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string name) => Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

public enum UpdateCheckStatus { UpdateAvailable, UpToDate, NoReleases, Offline, Error }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release, string Message);

/// <summary>Where updates come from. One place, never configurable by downloaded content.</summary>
public static class UpdateSource
{
    public const string Owner = "reprinted";
    public const string Repository = "NOXEL-Releases";
    public const string LatestReleaseApi = "https://api.github.com/repos/reprinted/NOXEL-Releases/releases/latest";
    public const string ReleasesPage = "https://github.com/reprinted/NOXEL-Releases/releases";
    public const string PortableAsset = "NOXEL.exe";
    public const string InstallerAsset = "NOXEL-Setup.exe";
    public const string ChecksumAsset = "SHA256SUMS.txt";
    public const long MaxAssetBytes = 400L * 1024 * 1024;

    /// <summary>Downloads are only accepted over HTTPS from GitHub's own hosts (or loopback during local tests).</summary>
    public static bool IsTrustedDownloadUri(Uri? u, bool allowLoopback = false)
    {
        if (u is null) return false;
        if (allowLoopback && u.IsLoopback && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)) return true;
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        var h = u.Host.ToLowerInvariant();
        return h == "github.com" || h == "api.github.com" || h.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }
}

/// <summary>Parses GitHub's "latest release" JSON defensively; invalid data yields null with a reason.</summary>
public static class GitHubReleaseParser
{
    public static ReleaseInfo? Parse(string json, out string error, bool allowLoopback = false)
    {
        error = "";
        if (json.Length > 2_000_000) { error = "Release data is unexpectedly large."; return null; }
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { error = "Release data isn't an object."; return null; }
            var tag = Str(r, "tag_name");
            if (!SemVer.TryParse(tag, out var version)) { error = $"The release tag \"{tag}\" isn't a valid version (expected e.g. v1.0.1)."; return null; }
            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray().Take(50))
                {
                    var name = Str(a, "name");
                    var url = Str(a, "browser_download_url");
                    if (name.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !UpdateSource.IsTrustedDownloadUri(uri, allowLoopback)) continue;
                    long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : -1;
                    assets.Add(new ReleaseAsset(name, size, uri, DigestToSha256(Str(a, "digest"))));
                }
            }
            DateTimeOffset? published = DateTimeOffset.TryParse(Str(r, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var p) ? p : null;
            Uri.TryCreate(Str(r, "html_url"), UriKind.Absolute, out var page);
            if (page is not null && !UpdateSource.IsTrustedDownloadUri(page, allowLoopback)) page = null;
            var notes = Str(r, "body");
            if (notes.Length > 20_000) notes = notes[..20_000] + "…";
            return new ReleaseInfo(version, tag, Str(r, "name") is { Length: > 0 } t ? t : tag, notes, published, page,
                Bool(r, "draft"), Bool(r, "prerelease"), assets);
        }
        catch (JsonException)
        {
            error = "GitHub's reply couldn't be read as release data.";
            return null;
        }
    }

    /// <summary>GitHub reports asset digests as "sha256:&lt;hex&gt;".</summary>
    public static string? DigestToSha256(string? digest)
    {
        if (string.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
        var hex = digest[7..];
        return IsSha256Hex(hex) ? hex.ToLowerInvariant() : null;
    }

    public static bool IsSha256Hex(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    /// <summary>Parses "&lt;hex&gt;  &lt;file&gt;" lines (sha256sum / Get-FileHash style). Unknown lines are ignored.</summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n').Take(200))
        {
            var line = raw.Trim();
            if (line.Length < 66 || line.StartsWith('#')) continue;
            var hash = line[..64];
            var name = line[64..].Trim().TrimStart('*').Trim();
            if (IsSha256Hex(hash) && name.Length > 0 && !name.Contains('/') && !name.Contains('\\')) map[name] = hash.ToLowerInvariant();
        }
        return map;
    }

    private static string Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

public static class UpdateDecision
{
    /// <summary>Stable channel: drafts and prereleases are never offered.</summary>
    public static UpdateCheckResult Evaluate(SemVer installed, ReleaseInfo? release)
    {
        if (release is null) return new(UpdateCheckStatus.NoReleases, null, "No releases have been published yet.");
        if (release.Draft || release.Prerelease || release.Version.IsPrerelease)
            return new(UpdateCheckStatus.UpToDate, null, "The latest published release is a preview; the stable channel ignores it.");
        return release.Version > installed
            ? new(UpdateCheckStatus.UpdateAvailable, release, $"NOXEL v{release.Version} is available.")
            : new(UpdateCheckStatus.UpToDate, release, $"You're up to date (v{installed}).");
    }
}
