using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Media.Imaging;

namespace CastorApplication.Services;

/// <summary>
/// Extracts a single representative frame from a video file using ffmpeg.
/// Used as a static visual fallback when LibVLC isn't available (macOS arm64).
/// </summary>
public static class ThumbnailService
{
    public static Bitmap? Extract(string videoPath, double atSeconds = 3.0)
    {
        if (!File.Exists(videoPath)) return null;
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg == null) { Console.Error.WriteLine("[Thumb] ffmpeg not found in PATH"); return null; }

        var tmp = Path.Combine(Path.GetTempPath(), $"castor_thumb_{Guid.NewGuid():N}.jpg");
        var psi = new ProcessStartInfo
        {
            FileName               = ffmpeg,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-ss"); psi.ArgumentList.Add(atSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(videoPath);
        psi.ArgumentList.Add("-vframes"); psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add("scale=640:-2");
        psi.ArgumentList.Add(tmp);

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return null;
            if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return null; }
            if (!File.Exists(tmp)) return null;
            var bmp = new Bitmap(tmp);
            try { File.Delete(tmp); } catch { }
            return bmp;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Thumb] failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extract a sequence of evenly-spaced frames. Used to fake video playback
    /// when LibVLC isn't available — cycling these in the UI gives a slowed
    /// motion preview.
    /// </summary>
    public static System.Collections.Generic.List<Bitmap> ExtractSequence(string videoPath, int fps = 10, int maxFrames = 800)
    {
        var result = new System.Collections.Generic.List<Bitmap>();
        if (!File.Exists(videoPath)) return result;
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg == null) return result;

        var dir = Path.Combine(Path.GetTempPath(), $"castor_seq_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var pattern = Path.Combine(dir, "f%03d.jpg");

            var psi = new ProcessStartInfo
            {
                FileName               = ffmpeg,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardError  = true,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(videoPath);
            psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add($"fps={fps},scale=480:-2");
            psi.ArgumentList.Add("-vframes"); psi.ArgumentList.Add(maxFrames.ToString());
            psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add("6");
            psi.ArgumentList.Add(pattern);

            using var p = Process.Start(psi);
            if (p == null) return result;
            if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } }

            foreach (var f in System.Linq.Enumerable.OrderBy(Directory.GetFiles(dir, "*.jpg"), x => x))
            {
                try { result.Add(new Bitmap(f)); } catch { }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Thumb] seq failed: {ex.Message}"); }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
        Console.Error.WriteLine($"[Thumb] extracted {result.Count} frames for {Path.GetFileName(videoPath)}");
        return result;
    }

    private static string? ResolveFfmpeg()
    {
        var exeNames = OperatingSystem.IsWindows() ? new[] { "ffmpeg.exe", "ffmpeg" } : new[] { "ffmpeg" };

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                foreach (var name in exeNames)
                {
                    try
                    {
                        var p = Path.Combine(dir, name);
                        if (File.Exists(p)) return p;
                    }
                    catch { }
                }
            }
        }
        if (!OperatingSystem.IsWindows())
        {
            foreach (var p in new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg" })
                if (File.Exists(p)) return p;
        }
        return null;
    }
}
