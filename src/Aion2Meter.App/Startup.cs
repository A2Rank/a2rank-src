using Microsoft.Win32;

namespace Aion2Meter.App;

/// <summary>Start with Windows: one value under the current user's Run key (no administrator rights, nothing for other users).</summary>
internal static class Startup
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "A2Rank";

    /// <summary>Makes the Run key match the setting: with it on, the value points at this exe (so an update or a moved install fixes itself at the next start); off removes it.</summary>
    public static void Apply(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(KeyPath);
            if (on)
            {
                string exe = Environment.ProcessPath ?? "";
                if (exe.Length > 0 && key.GetValue(ValueName) as string != $"\"{exe}\"") key.SetValue(ValueName, $"\"{exe}\"");
            }
            else if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Write("startup entry: " + e.Message);
        }
    }
}
