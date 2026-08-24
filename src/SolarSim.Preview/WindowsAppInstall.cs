using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace SolarSim.Preview;

/// <summary>
/// Registers this per-user copy in Programs and Features so it can be uninstalled
/// without an MSI. Does not touch .solarproj files.
/// </summary>
internal static class WindowsAppInstall
{
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\solarSim";

    public static bool TryHandleCommandLine(string[] args)
    {
        if (!args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            return false;

        var quiet = args.Any(a => a.Equals("--quiet", StringComparison.OrdinalIgnoreCase));
        Uninstall(quiet);
        return true;
    }

    public static void RegisterThisCopy()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return;

            var dir = Path.GetDirectoryName(exe) ?? "";
            var version = AppVersion();
            var sizeKb = Math.Max(1, (int)(new FileInfo(exe).Length / 1024));

            using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
            if (key is null) return;

            key.SetValue("DisplayName", "solarSim");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "solarSim");
            key.SetValue("InstallLocation", dir);
            key.SetValue("DisplayIcon", exe);
            key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
            key.SetValue("HelpLink", ExternalLinks.Repo);
            key.SetValue("URLInfoAbout", ExternalLinks.Repo);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);

            TryCreateStartMenuShortcut(exe, dir);
        }
        catch
        {
            // Portable copy still runs if registry/shortcut is blocked.
        }
    }

    public static void Uninstall(bool quiet)
    {
        if (!quiet)
        {
            var answer = System.Windows.MessageBox.Show(
                "Remove solarSim from this PC?\n\nYour .solarproj project files are not deleted.",
                "solarSim",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.Yes)
                return;
        }

        var exe = Environment.ProcessPath;
        TryDeleteStartMenuShortcut();
        TryDeleteAppData();
        TryDeleteUninstallKey();

        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return;

        TryScheduleDeleteExe(exe, Environment.ProcessId);
    }

    private static void TryCreateStartMenuShortcut(string exe, string workingDir)
    {
        var link = StartMenuShortcutPath();
        var programs = Path.GetDirectoryName(link);
        if (!string.IsNullOrEmpty(programs))
            Directory.CreateDirectory(programs);

        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) return;
        var shell = Activator.CreateInstance(type);
        if (shell is null) return;
        var shortcut = type.InvokeMember(
            "CreateShortcut",
            BindingFlags.InvokeMethod,
            null,
            shell,
            [link]);
        if (shortcut is null) return;
        var scType = shortcut.GetType();
        scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [exe]);
        scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [workingDir]);
        scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [exe]);
        scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, ["solarSim"]);
        scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
    }

    private static void TryDeleteStartMenuShortcut()
    {
        try
        {
            var link = StartMenuShortcutPath();
            if (File.Exists(link))
                File.Delete(link);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDeleteAppData()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "solarSim");
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDeleteUninstallKey()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryScheduleDeleteExe(string exe, int pid)
    {
        try
        {
            var script = Path.Combine(Path.GetTempPath(), "solarsim-uninstall.cmd");
            File.WriteAllText(script, $"""
@echo off
setlocal
set "PID={pid}"
set "EXE={exe}"
:wait
tasklist /FI "PID eq %PID%" 2>NUL | find "%PID%" >NUL
if not errorlevel 1 (
  timeout /t 1 /nobreak >NUL
  goto wait
)
del /f /q "%EXE%" >NUL 2>&1
del /f /q "%~f0" >NUL 2>&1
""");
            Process.Start(new ProcessStartInfo
            {
                FileName = script,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch
        {
            // Registry and app data are already gone; leftover exe can be deleted by hand.
        }
    }

    private static string StartMenuShortcutPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            "solarSim.lnk");

    private static string AppVersion()
    {
        var asm = typeof(WindowsAppInstall).Assembly;
        var info = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "1.5.13" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
