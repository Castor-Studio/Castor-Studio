using System.Diagnostics;
using CastorApplication.Services.Studio;
using LibObs;

namespace CastorApplication.Services.Ai;

/// <summary>
/// Hooks the audio of each microphone source (<see cref="ObsSource.AddAudioCaptureCallback"/>), after
/// its filters and before the mixer, and hands it to the AI as mono float32. Removing the source
/// from its scene releases the hook with it.
/// </summary>
internal sealed class LibObsAiAudioCaptureRuntime(IAiSceneSourceProvider sources) : IAiAudioCaptureRuntime
{
    private readonly object _gate = new();
    private readonly List<MicrophoneCapture> _captures = [];

    public void Start(IReadOnlyDictionary<AiMicrophone, int> channels, AiAudioSink sink)
    {
        StopAll();
        var failures = new List<string>();
        lock (_gate)
        {
            foreach (var (microphone, channel) in channels)
            {
                var capture = new MicrophoneCapture(channel, sink);
                try
                {
                    var registration = sources.AddSourceAudioCapture(microphone.SceneId, microphone.SourceId, capture.OnAudio);
                    if (registration == null)
                    {
                        failures.Add("Un micro choisi pour l'IA n'existe plus dans sa scène.");
                        continue;
                    }

                    capture.Registration = registration;
                    _captures.Add(capture);
                }
                catch (Exception exception)
                {
                    failures.Add($"Capture d'un micro impossible : {exception.Message}");
                }
            }
        }

        if (failures.Count > 0) throw new InvalidOperationException(string.Join(" ", failures.Distinct()));
    }

    public void StopAll()
    {
        MicrophoneCapture[] captures;
        lock (_gate)
        {
            captures = _captures.ToArray();
            _captures.Clear();
        }

        // Once Dispose returns, libobs runs no callback of that registration any more.
        foreach (var capture in captures)
        {
            try { capture.Registration?.Dispose(); }
            catch (Exception exception) { Debug.WriteLine($"[Castor.AI audio] release failed: {exception.Message}"); }
        }
    }

    public void Dispose() => StopAll();

    private sealed class MicrophoneCapture(int channel, AiAudioSink sink)
    {
        // Grown only when libobs sends a bigger block than before: no allocation per block.
        private float[] _mono = new float[1024];

        public ObsSourceAudioCapture? Registration { get; set; }

        // libobs audio thread, under the source's audio callback lock.
        public void OnAudio(ObsSourceAudioFrame frame)
        {
            var frames = (int)frame.Frames;
            if (frames == 0) return;
            if (_mono.Length < frames) _mono = new float[frames];
            var mono = _mono.AsSpan(0, frames);
            mono.Clear();

            // Source audio is float planar: average the channels that carry data.
            var planes = 0;
            for (var plane = 0; plane < frame.PlaneCount; plane++)
            {
                var samples = frame.GetFloatSamples(plane);
                if (samples.Length < frames) continue;
                for (var index = 0; index < frames; index++) mono[index] += samples[index];
                planes++;
            }

            if (planes == 0) return;
            if (planes > 1)
            {
                var scale = 1f / planes;
                for (var index = 0; index < frames; index++) mono[index] *= scale;
            }

            sink(channel, mono, (int)frame.SamplesPerSecond);
        }
    }
}
