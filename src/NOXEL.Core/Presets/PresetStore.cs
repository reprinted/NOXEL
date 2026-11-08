using System.Text.Json;
using Noxel.Core.Color;
using Noxel.Core.Hotkeys;
using Noxel.Core.Infrastructure;

namespace Noxel.Core.Presets;

/// <summary>On-disk shape of presets.json.</summary>
public sealed class PresetFileModel
{
    public string Format { get; set; } = PresetStore.FormatId;
    public int Version { get; set; } = 1;
    public List<Preset> Presets { get; set; } = new();
    /// <summary>User state for built-ins (favorite flag, hotkey). Built-in values themselves ship with the app.</summary>
    public Dictionary<Guid, BuiltInOverride> BuiltInOverrides { get; set; } = new();
}

public sealed class BuiltInOverride
{
    public bool IsFavorite { get; set; }
    public string? Hotkey { get; set; }
}

/// <summary>
/// Owns all presets. Thread-affine to the caller (UI thread); persistence is atomic.
/// </summary>
public sealed class PresetStore
{
    public const string FormatId = "noxel-presets";
    private readonly string _path;
    private readonly List<Preset> _user = new();
    private readonly Dictionary<Guid, BuiltInOverride> _overrides = new();

    public event EventHandler? Changed;
    public string? LoadWarning { get; private set; }

    public PresetStore(string path) { _path = path; }

    public IReadOnlyList<Preset> All =>
        BuiltInPresets.All.Select(ApplyOverride).Concat(_user).ToList();

    public IEnumerable<Preset> Favorites => All.Where(p => p.IsFavorite);

    public Preset? Find(Guid id) => All.FirstOrDefault(p => p.Id == id);

    public void Load()
    {
        var model = JsonFile.LoadOrDefault(_path, () => new PresetFileModel(), w => LoadWarning = w);
        _user.Clear();
        _overrides.Clear();
        foreach (var p in model.Presets ?? new())
        {
            var s = PresetValidator.Sanitize(p, keepId: true);
            if (s is null || BuiltInPresets.All.Any(b => b.Id == s.Id) || _user.Any(u => u.Id == s.Id)) continue;
            _user.Add(s with { IsBuiltIn = false });
        }
        foreach (var (id, o) in model.BuiltInOverrides ?? new())
        {
            if (BuiltInPresets.All.Any(b => b.Id == id))
                _overrides[id] = new BuiltInOverride { IsFavorite = o.IsFavorite, Hotkey = HotkeyGesture.Normalize(o.Hotkey) };
        }
        EnsureCustomSlot();
        // Defaults for built-in favorites on first run.
        foreach (var b in BuiltInPresets.All)
            if (!_overrides.ContainsKey(b.Id)) _overrides[b.Id] = new BuiltInOverride { IsFavorite = b.IsFavorite };
    }

    public void Save()
    {
        var model = new PresetFileModel { Presets = _user.ToList(), BuiltInOverrides = new(_overrides) };
        JsonFile.Save(_path, model);
    }

    private void EnsureCustomSlot()
    {
        if (!_user.Any(p => p.Id == BuiltInPresets.CustomId))
            _user.Insert(0, BuiltInPresets.CustomSlot());
    }

    private Preset ApplyOverride(Preset b) =>
        _overrides.TryGetValue(b.Id, out var o) ? b with { IsFavorite = o.IsFavorite, Hotkey = o.Hotkey } : b;

    public Preset Add(Preset p)
    {
        var s = PresetValidator.Sanitize(p with { Id = Guid.NewGuid(), IsBuiltIn = false, Created = DateTimeOffset.Now, Modified = DateTimeOffset.Now }, keepId: true)
                ?? throw new ArgumentException("Invalid preset");
        s = s with { Name = UniqueName(s.Name) };
        _user.Add(s);
        Commit();
        return s;
    }

    /// <summary>Updates a user preset. Built-ins only accept favorite/hotkey changes.</summary>
    public Preset Update(Preset p)
    {
        if (BuiltInPresets.All.Any(b => b.Id == p.Id))
        {
            _overrides[p.Id] = new BuiltInOverride { IsFavorite = p.IsFavorite, Hotkey = HotkeyGesture.Normalize(p.Hotkey) };
            Commit();
            return Find(p.Id)!;
        }
        int i = _user.FindIndex(u => u.Id == p.Id);
        if (i < 0) throw new KeyNotFoundException("Preset no longer exists.");
        var s = PresetValidator.Sanitize(p with { IsBuiltIn = false, Modified = DateTimeOffset.Now, Created = _user[i].Created }, keepId: true)
                ?? throw new ArgumentException("Invalid preset");
        if (!string.Equals(s.Name, _user[i].Name, StringComparison.CurrentCultureIgnoreCase))
            s = s with { Name = UniqueName(s.Name, except: s.Id) };
        _user[i] = s;
        Commit();
        return s;
    }

    public void SetFavorite(Guid id, bool favorite)
    {
        var p = Find(id);
        if (p is not null) Update(p with { IsFavorite = favorite });
    }

    public Preset Duplicate(Guid id)
    {
        var src = Find(id) ?? throw new KeyNotFoundException();
        return Add(src with { Name = src.Name + " copy", IsFavorite = false, Hotkey = null });
    }

    public bool CanDelete(Guid id) => !BuiltInPresets.IsProtected(id) && _user.Any(u => u.Id == id);

    public void Delete(Guid id)
    {
        if (!CanDelete(id)) throw new InvalidOperationException("Built-in presets can't be deleted. Duplicate one to make your own copy.");
        _user.RemoveAll(u => u.Id == id);
        Commit();
    }

    public void RestoreBuiltIns()
    {
        _overrides.Clear();
        foreach (var b in BuiltInPresets.All) _overrides[b.Id] = new BuiltInOverride { IsFavorite = b.IsFavorite };
        int i = _user.FindIndex(u => u.Id == BuiltInPresets.CustomId);
        if (i >= 0) _user[i] = BuiltInPresets.CustomSlot();
        Commit();
    }

    public IEnumerable<Preset> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return All;
        var q = query.Trim();
        return All.Where(p => p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                           || p.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                           || p.Summary().Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }

    // ---------- Import / export ----------

    public string Export(IEnumerable<Guid> ids)
    {
        var list = ids.Select(Find).Where(p => p is not null)
            .Select(p => p! with { IsBuiltIn = false, Hotkey = null }).ToList();
        var model = new PresetFileModel { Presets = list, BuiltInOverrides = new() };
        return JsonSerializer.Serialize(model, NoxelJson.Options);
    }

    /// <summary>Imports presets from untrusted JSON. Never throws on bad data; returns what was imported and why others were skipped.</summary>
    public (List<Preset> Imported, List<string> Skipped) Import(string json)
    {
        var imported = new List<Preset>();
        var skipped = new List<string>();
        if (json.Length > 1_000_000) { skipped.Add("File is larger than 1 MB."); return (imported, skipped); }

        List<Preset> candidates;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            candidates = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Object when doc.RootElement.TryGetProperty("presets", out _) =>
                    doc.RootElement.Deserialize<PresetFileModel>(NoxelJson.Options)?.Presets ?? new(),
                JsonValueKind.Object => new() { doc.RootElement.Deserialize<Preset>(NoxelJson.Options)! },
                JsonValueKind.Array => doc.RootElement.Deserialize<List<Preset>>(NoxelJson.Options) ?? new(),
                _ => throw new JsonException("Expected a NOXEL preset file."),
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            skipped.Add("Not a valid NOXEL preset file: " + ex.Message);
            return (imported, skipped);
        }

        foreach (var c in candidates.Take(200))
        {
            if (c is null) { skipped.Add("Empty entry."); continue; }
            var s = PresetValidator.Sanitize(c, keepId: false);
            if (s is null) { skipped.Add($"'{c.Name}' has no display or color settings."); continue; }
            s = s with { Hotkey = null, IsFavorite = false, IsBuiltIn = false, Name = UniqueName(s.Name) };
            _user.Add(s);
            imported.Add(s);
        }
        if (candidates.Count > 200) skipped.Add($"{candidates.Count - 200} presets beyond the 200 limit were ignored.");
        if (imported.Count > 0) Commit();
        return (imported, skipped);
    }

    private string UniqueName(string name, Guid? except = null)
    {
        var existing = All.Where(p => p.Id != except).Select(p => p.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!existing.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            var n = $"{name} ({i})";
            if (!existing.Contains(n)) return n;
        }
    }

    private void Commit()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public static class PresetValidator
{
    public const int MaxName = 48, MaxDescription = 160;

    /// <summary>Returns a clamped, cleaned copy of a preset, or null if it carries nothing usable.</summary>
    public static Preset? Sanitize(Preset p, bool keepId)
    {
        if (p is null) return null;
        var name = Clean(p.Name, MaxName);
        if (string.IsNullOrWhiteSpace(name)) name = "Untitled preset";
        PresetDisplay? display = null;
        if (p.Display is { } d)
        {
            bool keep = d.Width == 0 || d.Height == 0;
            bool valid = keep || (d.Width is >= 320 and <= 16384 && d.Height is >= 200 and <= 16384);
            if (valid && d.RefreshHz is >= 0 and <= 1000)
                display = keep ? new PresetDisplay { RefreshHz = d.RefreshHz } : new PresetDisplay { Width = d.Width, Height = d.Height, RefreshHz = d.RefreshHz };
        }
        var color = p.Color?.Clamped();
        if (display is null && color is null) return null;
        var monitorId = p.MonitorId is { Length: > 0 and <= 512 } ? p.MonitorId : null;
        return p with
        {
            Id = keepId && p.Id != Guid.Empty ? p.Id : Guid.NewGuid(),
            Name = name,
            Description = Clean(p.Description, MaxDescription),
            MonitorId = monitorId,
            MonitorName = monitorId is null ? null : Clean(p.MonitorName, 64),
            Display = display,
            Color = color,
            Hotkey = HotkeyGesture.Normalize(p.Hotkey),
            Created = Sane(p.Created),
            Modified = Sane(p.Modified),
        };
    }

    private static string Clean(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var chars = s.Where(c => !char.IsControl(c)).ToArray();
        var r = new string(chars).Trim();
        return r.Length > max ? r[..max] : r;
    }

    private static DateTimeOffset Sane(DateTimeOffset d) =>
        d.Year is < 2000 or > 2200 ? DateTimeOffset.Now : d;
}
