using System.Globalization;
using System.Text;
using System.Text.Json;
using Noxel.Core.Infrastructure;
using Noxel.Core.Presets;

namespace Noxel.Core.Color;

/// <summary>
/// Import/export of a colour look on its own (no display mode). Accepted input:
///  • a NOXEL look code:  NOXEL-LOOK v=85 s=129 b=2 c=108 g=1.00 t=0   (keys in any order, missing keys = neutral)
///  • a NOXEL preset file or single preset (the first preset with colours is used)
///  • a bare colour JSON object: { "vibrance": 85, "saturation": 129, ... }
/// Values are clamped to the supported ranges; nothing in the input is executed or trusted.
/// </summary>
public static class LookCode
{
    public const string Prefix = "NOXEL-LOOK";
    public const int MaxInputChars = 1_000_000;

    public sealed record Result(ColorSettings? Settings, string? Name, string? Error)
    {
        public bool Success => Settings is not null;
    }

    public static string Encode(ColorSettings s)
    {
        s = s.Clamped();
        var ci = CultureInfo.InvariantCulture;
        return string.Create(ci, $"{Prefix} v={s.Vibrance:0} s={s.Saturation:0} b={s.Brightness:0} c={s.Contrast:0} g={s.Gamma:0.00} t={s.Temperature:0}");
    }

    public static Result Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new(null, null, "Paste a look code, or choose a preset file.");
        if (input.Length > MaxInputChars) return new(null, null, "That's too large to be a NOXEL look.");
        var text = input.Trim();

        if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return ParseCode(text);
        if (text.StartsWith('{') || text.StartsWith('[')) return ParseJson(text);
        return new(null, null, $"Not recognised. A look code starts with \"{Prefix}\"; preset files are JSON.");
    }

    private static Result ParseCode(string text)
    {
        var s = ColorSettings.Default;
        int found = 0;
        foreach (var token in text[Prefix.Length..].Split(new[] { ' ', '\t', '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0) continue;
            var key = token[..eq].Trim().ToLowerInvariant();
            if (!double.TryParse(token[(eq + 1)..].Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
                return new(null, null, $"\"{token}\" isn't a number.");
            s = key switch
            {
                "v" or "vibrance" => s with { Vibrance = v },
                "s" or "saturation" => s with { Saturation = v },
                "b" or "brightness" => s with { Brightness = v },
                "c" or "contrast" => s with { Contrast = v },
                "g" or "gamma" => s with { Gamma = v },
                "t" or "temperature" => s with { Temperature = v },
                _ => s,
            };
            found++;
        }
        return found == 0 ? new(null, null, "The code has no values.") : new(s.Clamped(), null, null);
    }

    private static Result ParseJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;

            // Preset file / single preset / array of presets → reuse the preset importer's validation.
            if (root.ValueKind == JsonValueKind.Array || (root.ValueKind == JsonValueKind.Object && (Has(root, "presets") || Has(root, "color"))))
            {
                var store = new PresetStore(Path.Combine(Path.GetTempPath(), $"noxel-look-{Guid.NewGuid():N}.json"));
                try
                {
                    var (imported, skipped) = store.Import(text);
                    var withColor = imported.FirstOrDefault(p => p.Color is not null);
                    if (withColor is not null) return new(withColor.Color, withColor.Name, null);
                    return new(null, null, skipped.FirstOrDefault() ?? "That preset has no colour settings.");
                }
                finally { TryDelete(store); }
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                var look = root.Deserialize<ColorSettings>(NoxelJson.Options);
                if (look is null) return new(null, null, "Empty colour object.");
                bool any = new[] { "vibrance", "saturation", "brightness", "contrast", "gamma", "temperature" }.Any(k => Has(root, k));
                return any ? new(look.Clamped(), null, null) : new(null, null, "No colour values found in that JSON.");
            }
            return new(null, null, "Not a NOXEL look.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return new(null, null, "That JSON couldn't be read: " + ex.Message);
        }
    }

    private static bool Has(JsonElement o, string name) =>
        o.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void TryDelete(PresetStore _)
    {
        try
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), "noxel-look-*.json*")) File.Delete(f);
        }
        catch { }
    }
}
