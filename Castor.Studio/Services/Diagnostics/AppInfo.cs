using System.Diagnostics;
using System.Reflection;

namespace CastorApplication.Services.Diagnostics;

public static class AppInfo
{
    // The release workflow builds with -p:Version from the tag; the SDK appends "+<commit>",
    // which is noise next to a version number.
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "?";
        var metadata = informational.IndexOf('+');
        return metadata < 0 ? informational : informational[..metadata];
    }

    // Explorer on the folder, or on the folder with the file selected.
    public static void ShowInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }

            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warning($"Could not open {path} in Explorer", exception);
        }
    }
}
