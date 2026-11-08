using Noxel.Core.Color;
using Noxel.Core.Display;
using Noxel.Core.Hotkeys;
using Noxel.Core.Infrastructure;
using Noxel.Core.Presets;
using Noxel.Core.Settings;
using Xunit;

namespace Noxel.Tests;

public class ColorMathTests
{
    [Fact]
    public void DefaultSettings_ProduceIdentityMatrix()
    {
        var m = ColorMath.BuildColorMatrix(ColorSettings.Default);
        var id = ColorMath.IdentityMatrix();
        for (int i = 0; i < 25; i++) Assert.Equal(id[i], m[i], 5);
        Assert.True(ColorSettings.Default.MatrixIsIdentity);
        Assert.True(ColorSettings.Default.GammaIsDefault);
    }

    [Fact]
    public void ZeroSaturation_IsGrayscale_AndPreservesLuma()
    {
        var m = ColorMath.BuildSaturationMatrix(0);
        var (r, g, b) = ColorMath.Apply(m, 1, 0, 0);
        Assert.Equal(r, g, 4);
        Assert.Equal(g, b, 4);
        Assert.Equal(ColorMath.LumR, r, 4);
    }

    [Fact]
    public void Saturation_LeavesGreysUnchanged()
    {
        var m = ColorMath.BuildSaturationMatrix(170);
        var (r, g, b) = ColorMath.Apply(m, 0.5f, 0.5f, 0.5f);
        Assert.Equal(0.5f, r, 4); Assert.Equal(0.5f, g, 4); Assert.Equal(0.5f, b, 4);
    }

    [Fact]
    public void Contrast_PivotsAroundMidGrey_BrightnessOffsets()
    {
        var m = ColorMath.BuildColorMatrix(new ColorSettings { Contrast = 150 });
        Assert.Equal(0.5f, ColorMath.Apply(m, 0.5f, 0.5f, 0.5f).R, 4);
        Assert.Equal(0.875f, ColorMath.Apply(m, 0.75f, 0.75f, 0.75f).R, 4);
        var b = ColorMath.BuildColorMatrix(new ColorSettings { Brightness = 10 });
        Assert.Equal(0.6f, ColorMath.Apply(b, 0.5f, 0.5f, 0.5f).G, 4);
    }

    [Fact]
    public void WarmTemperature_ReducesBlueMoreThanGreen()
    {
        var m = ColorMath.BuildColorMatrix(new ColorSettings { Temperature = 100 });
        var (r, g, b) = ColorMath.Apply(m, 1, 1, 1);
        Assert.Equal(1f, r, 4);
        Assert.True(g < 1 && b < g);
    }

    [Fact]
    public void GammaRamp_IsMonotonic_AndIdentityAtOne()
    {
        Assert.Equal(ColorMath.IdentityRamp(), ColorMath.BuildGammaRamp(1.0));
        Assert.True(ColorMath.IsMonotonic(ColorMath.BuildGammaRamp(1.6)));
        Assert.True(ColorMath.BuildGammaRamp(1.6)[128] > ColorMath.IdentityRamp()[128]); // >1 lifts midtones
    }

    [Theory]
    [InlineData(50, 0, 100, 50, 50)]
    [InlineData(100, 0, 100, 50, 100)]
    [InlineData(0, 0, 100, 50, 0)]
    [InlineData(75, 0, 63, 0, 32)]
    [InlineData(25, 0, 63, 0, 0)]
    public void Vibrance_MapsToDriverLevels(double v, int min, int max, int def, int expected) =>
        Assert.Equal(expected, ColorMath.VibranceToDriverLevel(v, min, max, def));

    [Fact]
    public void Clamped_RejectsOutOfRangeAndNaN()
    {
        var s = new ColorSettings { Saturation = 999, Gamma = double.NaN, Brightness = -80 }.Clamped();
        Assert.Equal(200, s.Saturation);
        Assert.Equal(1.0, s.Gamma);
        Assert.Equal(-50, s.Brightness);
    }
}

public class DisplayModelTests
{
    [Theory]
    [InlineData(1920, 1080, "16:9")]
    [InlineData(1440, 1080, "4:3")]
    [InlineData(1680, 1050, "16:10")]
    [InlineData(1280, 1024, "5:4")]
    [InlineData(1568, 1080, "196:135")]
    [InlineData(1080, 1920, "9:16")]
    [InlineData(1366, 768, "≈16:9")]
    public void AspectLabels(int w, int h, string expected) => Assert.Equal(expected, AspectRatio.Label(w, h));

    [Fact]
    public void ResolutionGroups_SortRatesDescending_AndFindMax()
    {
        var modes = new[] { new DisplayMode(1440, 1080, 240), new DisplayMode(1440, 1080, 320), new DisplayMode(1920, 1080, 60), new DisplayMode(1440, 1080, 180) };
        var groups = ResolutionGroup.Build(modes);
        Assert.Equal(1920, groups[0].Width);
        var g = groups.Single(x => x.Width == 1440);
        Assert.Equal(new[] { 320, 240, 180 }, g.RefreshRates);
        Assert.Equal(320, g.MaxRefresh);
    }

    [Theory]
    [InlineData("1440x1080", 1440, 1080, 0)]
    [InlineData(" 1440 × 1080 ", 1440, 1080, 0)]
    [InlineData("1440*1080@240", 1440, 1080, 240)]
    [InlineData("1440x1080@240hz", 1440, 1080, 240)]
    public void ParsesResolutions(string text, int w, int h, int hz)
    {
        Assert.True(ResolutionParser.TryParse(text, out var pw, out var ph, out var phz));
        Assert.Equal((w, h, hz), (pw, ph, phz));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1440")]
    [InlineData("abcxdef")]
    [InlineData("99999x1080")]
    [InlineData("1440x1080@0")]
    public void RejectsBadResolutions(string text) => Assert.False(ResolutionParser.TryParse(text, out _, out _, out _));

    [Fact]
    public void RefreshFormat_ShowsFractionalRatesOnlyWhenNeeded()
    {
        Assert.Equal("320 Hz", RefreshFormat.Exact(319.998));
        Assert.Equal("59.94 Hz", RefreshFormat.Exact(59.94));
        Assert.Equal("119.88 Hz", RefreshFormat.Exact(119.88));
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl + Alt + F12", "Ctrl + Alt + F12")]
    [InlineData("alt+ctrl+f12", "Ctrl + Alt + F12")]
    [InlineData("F6", "F6")]
    [InlineData("Ctrl+Shift+1", "Ctrl + Shift + 1")]
    public void RoundTrips(string input, string expected) => Assert.Equal(expected, HotkeyGesture.Normalize(input));

    [Theory]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Nope")]
    [InlineData("A+B")]
    public void RejectsInvalid(string input) => Assert.Null(HotkeyGesture.Normalize(input));

    [Theory]
    [InlineData("A")]
    [InlineData("Shift+A")]
    [InlineData("Alt+F4")]
    [InlineData("Win+L")]
    [InlineData("F12")]
    public void ValidateBlocksUnsafeShortcuts(string g)
    {
        Assert.True(HotkeyGesture.TryParse(g, out var gesture));
        Assert.NotNull(gesture.Validate());
    }

    [Theory]
    [InlineData("F6")]
    [InlineData("Ctrl+Alt+F12")]
    [InlineData("Ctrl+Shift+1")]
    public void ValidateAllowsDefaults(string g)
    {
        Assert.True(HotkeyGesture.TryParse(g, out var gesture));
        Assert.Null(gesture.Validate());
    }
}

public class PresetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "noxel-tests-" + Guid.NewGuid().ToString("N"));
    public PresetTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private PresetStore NewStore()
    {
        var s = new PresetStore(Path.Combine(_dir, "presets.json"));
        s.Load();
        return s;
    }

    [Fact]
    public void BuiltInsAndCustomSlotExist_AndAreProtected()
    {
        var s = NewStore();
        Assert.Contains(s.All, p => p.Id == BuiltInPresets.CompetitiveId);
        Assert.Contains(s.All, p => p.Id == BuiltInPresets.CustomId);
        Assert.False(s.CanDelete(BuiltInPresets.OriginalId));
        Assert.False(s.CanDelete(BuiltInPresets.CustomId));
        Assert.Throws<InvalidOperationException>(() => s.Delete(BuiltInPresets.OriginalId));
    }

    [Fact]
    public void AddDuplicateDelete_PersistAcrossReload()
    {
        var s = NewStore();
        var p = s.Add(new Preset { Name = "Comp", Display = new PresetDisplay { Width = 1440, Height = 1080, RefreshHz = 0 }, Color = new ColorSettings { Saturation = 125 } });
        var dup = s.Duplicate(p.Id);
        Assert.Equal("Comp copy", dup.Name);
        s.SetFavorite(BuiltInPresets.VibrantId, true);
        var reloaded = NewStore();
        Assert.Contains(reloaded.All, x => x.Id == p.Id && x.Display!.Width == 1440);
        Assert.True(reloaded.Find(BuiltInPresets.VibrantId)!.IsFavorite);
        reloaded.Delete(dup.Id);
        Assert.DoesNotContain(NewStore().All, x => x.Id == dup.Id);
    }

    [Fact]
    public void BuiltInEdits_OnlyKeepFavoriteAndHotkey()
    {
        var s = NewStore();
        var orig = s.Find(BuiltInPresets.CinemaId)!;
        s.Update(orig with { Name = "Hacked", Hotkey = "Ctrl+Alt+F9", IsFavorite = true });
        var after = NewStore().Find(BuiltInPresets.CinemaId)!;
        Assert.Equal("Cinema", after.Name);
        Assert.Equal("Ctrl + Alt + F9", after.Hotkey);
        Assert.True(after.IsFavorite);
    }

    [Fact]
    public void Import_IgnoresGarbage_AndClampsValues()
    {
        var s = NewStore();
        var (none, skipped) = s.Import("{ this is not json");
        Assert.Empty(none);
        Assert.NotEmpty(skipped);

        var json = """
        { "format": "noxel-presets", "version": 1, "presets": [
          { "id": "6f1d1b8e-0000-4000-8000-000000000001", "name": "Evil\u0007Name", "isBuiltIn": true,
            "color": { "saturation": 5000, "gamma": -3 }, "hotkey": "Win+L" },
          { "name": "Empty" },
          { "name": "Bad display", "display": { "width": 99999, "height": 1 } }
        ] }
        """;
        var (imported, skipped2) = s.Import(json);
        var p = Assert.Single(imported);
        Assert.False(p.IsBuiltIn);
        Assert.NotEqual(BuiltInPresets.OriginalId, p.Id);
        Assert.Equal("EvilName", p.Name);
        Assert.Equal(200, p.Color!.Saturation);
        Assert.Equal(0.5, p.Color.Gamma);
        Assert.Null(p.Hotkey);
        Assert.Equal(2, skipped2.Count);
    }

    [Fact]
    public void Import_DeeplyNested_DoesNotCrash()
    {
        var s = NewStore();
        var deep = new string('[', 5000) + new string(']', 5000);
        var (imported, skipped) = s.Import(deep);
        Assert.Empty(imported);
        Assert.NotEmpty(skipped);
    }

    [Fact]
    public void ExportThenImport_RoundTrips()
    {
        var s = NewStore();
        var p = s.Add(new Preset { Name = "Trip", Color = new ColorSettings { Contrast = 110 } });
        var json = s.Export(new[] { p.Id });
        var (imported, _) = NewStore().Import(json);
        Assert.Equal(110, Assert.Single(imported).Color!.Contrast);
    }
}

public class PersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "noxel-tests-" + Guid.NewGuid().ToString("N"));
    public PersistenceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void CorruptSettings_FallBackToBackup()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        store.Load();
        store.Current.AccentHex = "#112233";
        store.Save();
        store.Current.AccentHex = "#445566";
        store.Save(); // previous version is now settings.json.bak
        File.WriteAllText(path, "{{{ corrupt");
        var again = new SettingsStore(path);
        again.Load();
        Assert.Equal("#112233", again.Current.AccentHex);
        Assert.NotNull(again.LoadWarning);
        Assert.NotEmpty(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void MissingFile_GivesDefaults_AndSanitizes()
    {
        var store = new SettingsStore(Path.Combine(_dir, "none.json"));
        store.Load();
        Assert.Equal(15, store.Current.ConfirmSeconds);
        Assert.Equal("F6", store.Current.Hotkeys[HotkeyAction.ToggleNativeStretched].Gesture);

        var s = new AppSettings { ConfirmSeconds = 999, AccentHex = "purple", GlassIntensity = double.NaN }.Sanitized();
        Assert.Equal(60, s.ConfirmSeconds);
        Assert.Equal("#9B7BFF", s.AccentHex);
        Assert.Equal(70, s.GlassIntensity);
    }
}

public class LookCodeTests
{
    [Fact]
    public void EncodeParse_RoundTrips()
    {
        var s = new ColorSettings { Vibrance = 85, Saturation = 129, Brightness = 2, Contrast = 108, Gamma = 1.0, Temperature = -12 };
        var code = LookCode.Encode(s);
        Assert.StartsWith("NOXEL-LOOK", code);
        var r = LookCode.Parse(code);
        Assert.True(r.Success);
        Assert.Equal(s, r.Settings);
    }

    [Fact]
    public void Parse_ClampsAndIgnoresUnknownKeys_MissingKeysAreNeutral()
    {
        var r = LookCode.Parse("noxel-look s=999 zzz=4 c=108%");
        Assert.True(r.Success);
        Assert.Equal(200, r.Settings!.Saturation);
        Assert.Equal(108, r.Settings.Contrast);
        Assert.Equal(50, r.Settings.Vibrance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("NOXEL-LOOK")]
    [InlineData("NOXEL-LOOK s=abc")]
    [InlineData("{ not json")]
    [InlineData("{ \"name\": \"x\" }")]
    public void Parse_RejectsBadInput(string input) => Assert.False(LookCode.Parse(input).Success);

    [Fact]
    public void Parse_AcceptsColorJson_AndPresetFiles()
    {
        Assert.Equal(140, LookCode.Parse("{ \"saturation\": 140 }").Settings!.Saturation);
        var preset = "{ \"format\": \"noxel-presets\", \"presets\": [ { \"name\": \"Night\", \"color\": { \"temperature\": 60 } } ] }";
        var r = LookCode.Parse(preset);
        Assert.Equal(60, r.Settings!.Temperature);
        Assert.Equal("Night", r.Name);
    }
}

public class SettingsMigrationTests
{
    [Fact]
    public void Version1_StopsObeyingWindowsAnimationSettingByDefault()
    {
        var s = new AppSettings { Version = 1, RespectSystemReducedMotion = true }.Sanitized();
        Assert.False(s.RespectSystemReducedMotion);
        Assert.Equal(2, s.Version);
        var explicitChoice = new AppSettings { Version = 2, RespectSystemReducedMotion = true }.Sanitized();
        Assert.True(explicitChoice.RespectSystemReducedMotion);
    }
}
