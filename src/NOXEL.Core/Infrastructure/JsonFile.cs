// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 reprinted

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Noxel.Core.Infrastructure;

public static class NoxelJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
            MaxDepth = 32,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}

/// <summary>
/// Crash-safe JSON persistence: writes to a temp file, flushes, then atomically replaces the target
/// while keeping the previous version as .bak. Loading falls back to .bak, then to defaults, and moves
/// unreadable files aside instead of deleting them.
/// </summary>
public static class JsonFile
{
    public const long MaxFileBytes = 4 * 1024 * 1024;

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, NoxelJson.Options);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes);
            fs.Flush(true);
        }
        if (File.Exists(path))
            File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(tmp, path);
    }

    public static T LoadOrDefault<T>(string path, Func<T> fallback, Action<string>? report = null) where T : class
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var info = new FileInfo(candidate);
                if (info.Length > MaxFileBytes) throw new InvalidDataException("file too large");
                using var fs = File.OpenRead(candidate);
                var v = JsonSerializer.Deserialize<T>(fs, NoxelJson.Options);
                if (v is not null)
                {
                    if (candidate != path) report?.Invoke($"{Path.GetFileName(path)} was unreadable; restored the backup copy.");
                    return v;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
            {
                report?.Invoke($"Could not read {Path.GetFileName(candidate)}: {ex.Message}");
                if (candidate == path) TryQuarantine(path);
            }
        }
        return fallback();
    }

    private static void TryQuarantine(string path)
    {
        try { File.Copy(path, path + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true); } catch { /* best effort */ }
    }
}
