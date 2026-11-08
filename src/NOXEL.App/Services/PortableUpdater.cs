using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Noxel.App.Services;

/// <summary>
/// Applies a portable update. This code runs inside the NEW, already-verified NOXEL.exe (started from
/// %LOCALAPPDATA%\NOXEL\updates\&lt;version&gt;\ by the old one) with:
///   --apply-portable-update --pid &lt;old process id&gt; --target "&lt;old exe path&gt;"
/// Safety rules: it only replaces the exact executable the old process was running (checked against the live
/// process before it exits), only a *.exe file, only after that process has exited; the previous version is kept
/// next to it as "&lt;name&gt;.previous.exe" and restored if the copy fails; nothing else is touched and no elevation is
/// requested. If the folder isn't writable, the user gets the manual-replacement instructions instead.
/// </summary>
internal static class PortableUpdater
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);

    public static int Run(string[] args)
    {
        string? target = Arg(args, "--target");
        int pid = int.TryParse(Arg(args, "--pid"), out var p) ? p : -1;
        var self = Environment.ProcessPath;
        if (target is null || pid <= 0 || self is null) return Error("The update helper was started incorrectly. Nothing was changed.");
        target = Path.GetFullPath(target);
        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
            return Error("The NOXEL program to update wasn't found. Nothing was changed.");
        if (string.Equals(Path.GetFullPath(self), target, StringComparison.OrdinalIgnoreCase))
            return Error("The update can't replace itself. Nothing was changed.");

        // The target must be exactly what the old NOXEL process is running.
        try
        {
            using var old = Process.GetProcessById(pid);
            var running = old.MainModule?.FileName;
            if (running is null || !string.Equals(Path.GetFullPath(running), target, StringComparison.OrdinalIgnoreCase))
                return Error("The running NOXEL doesn't match the file to update. Nothing was changed.");
            if (!old.WaitForExit(30_000)) return Error("NOXEL didn't close in time, so the update wasn't applied. Try again.");
        }
        catch (ArgumentException) { /* already exited: fine, but we could not confirm identity → require name match */
            if (!Path.GetFileName(target).Contains("NOXEL", StringComparison.OrdinalIgnoreCase))
                return Error("Couldn't confirm which program to update. Nothing was changed.");
        }

        var backup = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + ".previous.exe");
        bool backedUp = false;   // only a backup made in this run may be restored (never an older leftover)
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (!backedUp)
                {
                    if (File.Exists(backup)) File.SetAttributes(backup, FileAttributes.Normal); // a read-only leftover must not block updates
                    File.Copy(target, backup, overwrite: true);
                    File.SetAttributes(backup, FileAttributes.Normal);
                    backedUp = true;
                }
                File.Copy(self, target, overwrite: true);
                break;
            }
            catch (IOException) when (attempt < 20) { Thread.Sleep(500); }  // file still locked for a moment after exit
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (backedUp) { try { File.Copy(backup, target, overwrite: true); } catch { } }
                Error($"Windows didn't allow NOXEL to replace\n{target}\n\n({ex.Message})\n\nYour current version will reopen. To update manually, " +
                      $"close NOXEL and copy\n{self}\nover the old file.", openFolder: Path.GetDirectoryName(self));
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) }); } catch { }
                return 1;
            }
        }

        try { Process.Start(new ProcessStartInfo(target, "--updated") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) }); }
        catch (Exception ex) { return Error("Updated, but NOXEL couldn't be restarted: " + ex.Message); }
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Error(string message, string? openFolder = null)
    {
        MessageBoxW(IntPtr.Zero, message, "NOXEL update", 0x30 /* warning */);
        if (openFolder is not null) { try { Process.Start("explorer.exe", $"\"{openFolder}\""); } catch { } }
        return 1;
    }
}
