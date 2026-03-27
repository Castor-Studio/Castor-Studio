using System.Linq;
using System.Runtime.InteropServices;

namespace Castor.Native
{
    // ── Enums ─────────────────────────────────────────────────────────────────

    public enum CaptureSourceType
    {
        Window  = 0,
        Monitor = 1,
        Camera  = 2,
    }

    public enum AudioSourceType
    {
        LoopbackGlobal = 0,
        LoopbackWindow = 1,
        Microphone     = 2,
        CameraMic      = 3,
    }

    // ── Structs (layout identique au C natif, x64) ────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct CaptureSourceInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Label;

        public CaptureSourceType Type;

        // 4 octets de padding implicite pour aligner le pointeur void* à 8 octets
        private int _pad;

        public IntPtr Hwnd;
        public IntPtr HMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string SymbolicLink;

        public int Index;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct AudioSourceInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Label;

        public AudioSourceType Type;

        // 4 octets de padding implicite pour aligner le pointeur void* à 8 octets
        private int _pad;

        public IntPtr Hwnd;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string DeviceId;

        public int Index;
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    public static class CastorNative
    {
        private const string DllName = "castor_core";
        private const CallingConvention Convention = CallingConvention.Cdecl;

        // ── Version ──────────────────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern IntPtr get_version();

        public static string GetVersion()
        {
            IntPtr ptr = get_version();
            return Marshal.PtrToStringAnsi(ptr) ?? "unknown";
        }

        // ── Initialisation ────────────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern void source_registry_init();

        [DllImport(DllName, CallingConvention = Convention)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool video_capture_module_load();

        [DllImport(DllName, CallingConvention = Convention)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool audio_capture_module_load();

        /// <summary>
        /// Initialise le registre de sources et charge les modules vidéo et audio.
        /// Doit être appelé une seule fois au démarrage.
        /// </summary>
        public static void Initialize()
        {
            source_registry_init();

            if (!video_capture_module_load())
                throw new InvalidOperationException("castor_core: video_capture_module_load() a échoué.");

            if (!audio_capture_module_load())
                throw new InvalidOperationException("castor_core: audio_capture_module_load() a échoué.");
        }

        // ── Énumération des sources vidéo ─────────────────────────────────────

        [DllImport(DllName, CallingConvention = Convention)] 
        private static extern int video_capture_list_sources(
            [In, Out] CaptureSourceInfo[] sources, int maxCount);

        /// <summary>
        /// Retourne toutes les sources vidéo disponibles (fenêtres, moniteurs, caméras).
        /// </summary>
        public static CaptureSourceInfo[] ListVideoSources(int maxCount = 64)
        {
            var buffer = new CaptureSourceInfo[maxCount];
            int count  = video_capture_list_sources(buffer, maxCount);
            return buffer.Take(Math.Max(0, count)).ToArray();
        }

        // ── Énumération des sources audio ─────────────────────────────────────

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern int audio_capture_list_sources(
            [In, Out] AudioSourceInfo[] sources, int maxCount);

        /// <summary>
        /// Retourne toutes les sources audio disponibles (loopback, micros, micros caméra).
        /// </summary>
        public static AudioSourceInfo[] ListAudioSources(int maxCount = 64)
        {
            var buffer = new AudioSourceInfo[maxCount];
            int count  = audio_capture_list_sources(buffer, maxCount);
            return buffer.Take(Math.Max(0, count)).ToArray();
        }

        // ── Scènes ────────────────────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern IntPtr scene_create();

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern IntPtr scene_item_create(int width, int height);

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern int scene_item_add_source(IntPtr item, IntPtr source);

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern int scene_add_item(IntPtr scene, IntPtr item);

        // ── Sources (lifecycle) ───────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern IntPtr source_create(
            [MarshalAs(UnmanagedType.LPStr)] string id, IntPtr settings);

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern void source_activate(IntPtr src);

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern void source_deactivate(IntPtr src);

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern void source_destroy(IntPtr src);

        // ── Helpers publics ───────────────────────────────────────────────────

        /// <summary>Crée une nouvelle scène native vide. Retourne le pointeur scene_t*.</summary>
        public static IntPtr NativeCreateScene() => scene_create();

        /// <summary>Crée un source_t* de type vidéo à partir d'un CaptureSourceInfo.</summary>
        public static IntPtr NativeCreateVideoSource(CaptureSourceInfo info)
        {
            IntPtr buf = Marshal.AllocHGlobal(Marshal.SizeOf<CaptureSourceInfo>());
            try
            {
                Marshal.StructureToPtr(info, buf, false);
                return source_create("video_capture", buf);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>Crée un source_t* de type audio à partir d'un AudioSourceInfo.</summary>
        public static IntPtr NativeCreateAudioSource(AudioSourceInfo info)
        {
            IntPtr buf = Marshal.AllocHGlobal(Marshal.SizeOf<AudioSourceInfo>());
            try
            {
                Marshal.StructureToPtr(info, buf, false);
                return source_create("audio_capture", buf);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>Attache un source_t* à une scène avec les dimensions données.</summary>
        public static int NativeAddSourceToScene(IntPtr scenePtr, IntPtr sourcePtr,
                                                  int width = 1920, int height = 1080)
        {
            if (scenePtr == IntPtr.Zero || sourcePtr == IntPtr.Zero) return -1;
            IntPtr item = scene_item_create(width, height);
            if (item == IntPtr.Zero) return -2;
            scene_item_add_source(item, sourcePtr);
            // scene_add_item copie la struct → item n'est plus nécessaire
            return scene_add_item(scenePtr, item);
        }

        public static void NativeActivateSource(IntPtr src)   { if (src != IntPtr.Zero) source_activate(src);   }
        public static void NativeDeactivateSource(IntPtr src) { if (src != IntPtr.Zero) source_deactivate(src); }
        public static void NativeDestroySource(IntPtr src)    { if (src != IntPtr.Zero) source_destroy(src);    }

        // ── Recorder ─────────────────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern IntPtr recorder_create(IntPtr config);

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern int recorder_start(IntPtr rec);

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern void recorder_stop(IntPtr rec);

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern void recorder_destroy(IntPtr rec);

        public static IntPtr RecorderCreate(ref RecorderConfig config)
        {
            IntPtr buf = Marshal.AllocHGlobal(Marshal.SizeOf<RecorderConfig>());
            try
            {
                Marshal.StructureToPtr(config, buf, false);
                return recorder_create(buf);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern int recorder_switch_video_source(IntPtr rec, int streamIndex, IntPtr newSrc);

        public static int  RecorderStart(IntPtr rec)   => recorder_start(rec);
        public static void RecorderStop(IntPtr rec)    { if (rec != IntPtr.Zero) recorder_stop(rec);    }
        public static void RecorderDestroy(IntPtr rec) { if (rec != IntPtr.Zero) recorder_destroy(rec); }

        public static int RecorderSwitchVideoSource(IntPtr rec, int streamIndex, CaptureSourceInfo info)
        {
            IntPtr buf = Marshal.AllocHGlobal(Marshal.SizeOf<CaptureSourceInfo>());
            try
            {
                Marshal.StructureToPtr(info, buf, false);
                return recorder_switch_video_source(rec, streamIndex, buf);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        // ── Streaming service ─────────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Ansi)]
        private static extern int streaming_service_get_url(
            CastorServiceType type,
            [MarshalAs(UnmanagedType.LPStr)] string streamKey,
            System.Text.StringBuilder urlOut,
            int urlLen);

        [DllImport(DllName, CallingConvention = Convention)]
        private static extern IntPtr streaming_service_name(CastorServiceType type);

        public static string? GetStreamingUrl(CastorServiceType type, string streamKey)
        {
            var sb = new System.Text.StringBuilder(512);
            int ret = streaming_service_get_url(type, streamKey, sb, 512);
            return ret == 0 ? sb.ToString() : null;
        }

        public static string GetStreamingServiceName(CastorServiceType type)
            => Marshal.PtrToStringAnsi(streaming_service_name(type)) ?? "";
    }

    // ── Recorder structs ──────────────────────────────────────────────────────

    public enum CastorOutputType  { File = 0, Rtmp = 1 }
    public enum CastorServiceType { Custom = 0, Twitch = 1, YouTube = 2 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct OutputConfig
    {
        public CastorOutputType Type;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string Destination;

        public int VideoBitrateKbps;
        public int AudioBitrateKbps;
        public int GopSeconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct StreamConfig
    {
        public CaptureSourceInfo VideoSrc;
        public AudioSourceInfo   AudioSrc;
        public OutputConfig      Output;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct RecorderConfig
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public StreamConfig[] Streams;

        public int NumStreams;
        public int Fps;
    }
}
