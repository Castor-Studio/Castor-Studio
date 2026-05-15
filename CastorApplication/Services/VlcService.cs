using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace CastorApplication.Services;

/// <summary>
/// Shared LibVLC instance + helpers. Centralizes the macOS arm64 quirk of
/// pointing LibVLCSharp at the system VLC.app (the NuGet only ships x64 libs).
/// </summary>
public sealed class VlcService
{
    private static VlcService? _instance;
    public static VlcService Instance => _instance ??= new VlcService();

    public LibVLC? LibVLC { get; private set; }

    public string? LastError { get; private set; }

    private VlcService()
    {
        try
        {
            var libDir = ResolveVlcLibDir();
            Console.Error.WriteLine($"[Vlc] system VLC libDir: {libDir ?? "(null)"}");

            // LibVLCSharp's macOS resolver expects libvlc.dylib + libvlccore.dylib
            // (and the `plugins` directory) NEXT TO THE EXECUTABLE. The NuGet
            // package only ships Windows libs, so on Mac we link the system VLC
            // libs into our bin dir on first launch.
            string? stagedDir = null;
            if (libDir != null && OperatingSystem.IsMacOS())
            {
                stagedDir = StageMacVlcRuntime(libDir);
                ForceLoadVlcDylibs(stagedDir ?? libDir);
            }

            // Pass the staged dir (next to the exe) — that's where LibVLCSharp
            // expects to find the dylibs, and now they're there.
            LibVLCSharp.Shared.Core.Initialize(stagedDir);
            Console.Error.WriteLine($"[Vlc] Core.Initialize done (using {stagedDir ?? "(default)"})");

            // Try several arg sets — libvlc_new() on macOS can fail when it
            // can't write its plugin cache (plugins.dat) into a read-only
            // VLC.app/.../plugins dir. The first variant that succeeds wins.
            var attempts = new[]
            {
                new[] { "--no-plugins-cache", "--no-video-title-show", "--network-caching=150" },
                new[] { "--reset-plugins-cache", "--no-video-title-show", "--network-caching=150" },
                new[] { "--verbose=2", "--no-plugins-cache", "--no-video-title-show" },
                new[] { "--no-plugins-cache" },
                Array.Empty<string>(),
            };
            foreach (var args in attempts)
            {
                try
                {
                    LibVLC = new LibVLC(args);
                    Console.Error.WriteLine($"[Vlc] init OK ✓ (args: {string.Join(' ', args)})");
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Vlc] LibVLC ctor failed with args [{string.Join(' ', args)}]: {ex.Message}");
                }
            }
            if (LibVLC == null) throw new InvalidOperationException("All LibVLC init attempts failed");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Console.Error.WriteLine($"[Vlc] init failed: {ex}");
            LibVLC = null;
        }
    }

    /// <summary>
    /// Symlink VLC.app's dylibs + plugins into AppContext.BaseDirectory so
    /// LibVLCSharp finds them via its standard executable-relative lookup.
    /// Idempotent: re-runs are no-ops if links already exist.
    /// Returns the staged directory path (= bin/) or null on failure.
    /// </summary>
    private static string? StageMacVlcRuntime(string srcLibDir)
    {
        var bin = AppContext.BaseDirectory;
        try
        {
            // 1) Symlink dylibs (both real names and versioned aliases).
            foreach (var name in new[]
            {
                "libvlc.dylib", "libvlccore.dylib",
                "libvlc.5.dylib", "libvlccore.9.dylib",
            })
            {
                var src = Path.Combine(srcLibDir, name);
                var dst = Path.Combine(bin, name);
                if (!File.Exists(src)) continue;
                if (File.Exists(dst) || Directory.Exists(dst)) continue;
                try
                {
                    File.CreateSymbolicLink(dst, src);
                    Console.Error.WriteLine($"[Vlc] linked {name}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Vlc] link {name} failed ({ex.Message}); falling back to copy");
                    try { File.Copy(src, dst); }
                    catch (Exception ex2) { Console.Error.WriteLine($"[Vlc] copy {name} also failed: {ex2.Message}"); }
                }
            }

            // 2) Symlink the plugins directory (300+ codec/demuxer .dylibs).
            var contentsMacOS = Path.GetDirectoryName(srcLibDir);
            if (contentsMacOS != null)
            {
                var pluginsSrc = Path.Combine(contentsMacOS, "plugins");
                var pluginsDst = Path.Combine(bin, "plugins");
                if (Directory.Exists(pluginsSrc) &&
                    !Directory.Exists(pluginsDst) && !File.Exists(pluginsDst))
                {
                    try
                    {
                        Directory.CreateSymbolicLink(pluginsDst, pluginsSrc);
                        Console.Error.WriteLine($"[Vlc] linked plugins/");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Vlc] link plugins failed: {ex.Message}");
                    }
                }
                // Belt-and-suspenders: also set VLC_PLUGIN_PATH.
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginsSrc);
            }

            return bin;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Vlc] StageMacVlcRuntime failed: {ex.Message}");
            return null;
        }
    }

    private static void ForceLoadVlcDylibs(string libDir)
    {
        // libvlccore must come first (libvlc depends on it).
        foreach (var name in new[] { "libvlccore.dylib", "libvlc.dylib" })
        {
            var p = Path.Combine(libDir, name);
            if (!File.Exists(p)) continue;
            try
            {
                NativeLibrary.Load(p);
                Console.Error.WriteLine($"[Vlc] dlopen OK: {p}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Vlc] dlopen FAILED: {p}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Build a MediaPlayer that loops a file forever (or plays an RTMP URL).
    /// Optionally muted — used for multicam previews so audio plays from
    /// the Studio output only.
    /// </summary>
    public MediaPlayer? CreateLoopedPlayer(string fileOrUri, bool mute = false)
    {
        if (LibVLC == null)
        {
            Console.Error.WriteLine($"[Vlc] LibVLC is null — cannot create player for {fileOrUri} (LastError={LastError})");
            return null;
        }
        try
        {
            var player = new MediaPlayer(LibVLC) { Mute = mute };
            Media media;
            if (File.Exists(fileOrUri))
                media = new Media(LibVLC, new Uri(fileOrUri, UriKind.Absolute), ":input-repeat=65535");
            else
                media = new Media(LibVLC, new Uri(fileOrUri), ":network-caching=150");
            player.Play(media);
            Console.Error.WriteLine($"[Vlc] playing {fileOrUri}");
            return player;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Vlc] play failed for {fileOrUri}: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveVlcLibDir()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        foreach (var dir in new[]
        {
            "/Applications/VLC.app/Contents/MacOS/lib",
            "/opt/homebrew/lib",
            "/usr/local/lib",
        })
        {
            if (File.Exists(Path.Combine(dir, "libvlc.dylib"))) return dir;
        }
        return null;
    }
}
