namespace Noxel.Core.Infrastructure;

public static class AppPaths
{
    private static string? _root;

    /// <summary>%LOCALAPPDATA%\NOXEL. Overridable for tests.</summary>
    public static string Root
    {
        get => _root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOXEL");
        set => _root = value;
    }

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Presets => Path.Combine(Root, "presets.json");
    public static string Logs => Path.Combine(Root, "logs");
    /// <summary>Recovery journal: original colors/display state to restore after a crash.</summary>
    public static string RecoveryJournal => Path.Combine(Root, "recovery.json");
}
