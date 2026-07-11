using Microsoft.Win32;

namespace RSSMagnetCatcher.Infrastructure;

public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RRSMC";

    public static string BuildCommand(string executablePath)
    {
        return $"\"{Path.GetFullPath(executablePath)}\"";
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(ValueName, BuildCommand(executablePath));
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
