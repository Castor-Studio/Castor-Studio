using CastorCore.Frame;
using CastorCore.Input.Audio.Converter;
using FFMpegCore.Pipes;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace CastorCore.Input.Audio
{
    public class WasapiAudioCapture : IAudioInput, IDisposable
    {
        private readonly MMDevice _device;
        private readonly AudioCaptureConfig _config;
        private readonly IAudioConverter _converter;
        private WasapiCapture? _capture;

        private readonly ConcurrentQueue<AudioSample> _queue = new();
        private volatile bool _isRecording;

        private int _channels;
        private int _sampleRate;
        private int _bitsPerSample;
        private WaveFormat? _targetFormat;

        public MMDevice Device => _device;

        public WasapiAudioCapture(MMDevice device)
            : this(device, new AudioCaptureConfig())
        {
        }

        public WasapiAudioCapture(MMDevice device, AudioCaptureConfig config)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _converter = AudioConverterFactory.CreateConverter(_config.OutputFormat);
        }

        public void StartCapture()
        {
            if (_isRecording)
                return;

            _capture = CreateWasapiCapture(_device, _config);

            _channels = _capture.WaveFormat.Channels;
            _sampleRate = _capture.WaveFormat.SampleRate;
            _bitsPerSample = _capture.WaveFormat.BitsPerSample;

            _targetFormat = CreateTargetFormat(_capture.WaveFormat, _config);

            LogCaptureInfo(_capture.WaveFormat, _targetFormat);

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _capture.StartRecording();
            _isRecording = true;
        }

        private WasapiCapture? CreateWasapiCapture(MMDevice device, AudioCaptureConfig config)
        {
            WasapiCapture capture;

            if (device.DataFlow == DataFlow.Render)
                capture = new WasapiLoopbackCapture(device);
            else
                capture = new WasapiCapture(device);

            capture.ShareMode = config.ShareMode;

            return capture;
        }
        private WaveFormat? CreateTargetFormat(WaveFormat sourceFormat, AudioCaptureConfig config)
        {
            int targetSampleRate = config.SampleRate > 0
                ? config.SampleRate
                : sourceFormat.SampleRate;

            int targetChannels = config.Channels > 0
                ? config.Channels
                : sourceFormat.Channels;

            return config.OutputFormat switch
            {
                //TODO: Remove hardcoded 16 bits
                AudioFormat.PCM16 => new WaveFormat(targetSampleRate, 16, targetChannels),
                _ => throw new NotSupportedException($"Format {config.OutputFormat} not supported")
            };
        }

        private void LogCaptureInfo(WaveFormat sourceFormat, WaveFormat targetFormat)
        {
            Debug.WriteLine($"Audio Capture Started:");
            Debug.WriteLine($"  SOURCE FORMAT:");
            Debug.WriteLine($"    Sample Rate: {sourceFormat.SampleRate} Hz");
            Debug.WriteLine($"    Channels: {sourceFormat.Channels}");
            Debug.WriteLine($"    Bits per Sample: {sourceFormat.BitsPerSample}");
            Debug.WriteLine($"    Encoding: {sourceFormat.Encoding}");
            Debug.WriteLine($"  TARGET FORMAT:");
            Debug.WriteLine($"    Sample Rate: {targetFormat.SampleRate} Hz");
            Debug.WriteLine($"    Channels: {targetFormat.Channels}");
            Debug.WriteLine($"    Bits per Sample: {targetFormat.BitsPerSample}");
            Debug.WriteLine($"    Encoding: {targetFormat.Encoding}");
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0)
            {
                byte[] buffer = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, buffer, e.BytesRecorded);

                byte[] processedBuffer = _converter.Convert(buffer, _capture!.WaveFormat, _targetFormat!);

                _queue.Enqueue(new AudioSample(processedBuffer, _channels, _sampleRate));
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _isRecording = false;
            
            if (e.Exception != null)
            {
                Debug.WriteLine($"Recording stopped with error: {e.Exception.Message}");
            }
        }

        public void StopCapture()
        {
            if (!_isRecording)
                return;

            _capture?.StopRecording();
            _isRecording = false;
        }

        public IEnumerable<IAudioSample> PullSamples()
        {
            while (_isRecording || !_queue.IsEmpty)
            {
                if (_queue.TryDequeue(out AudioSample? sample))
                {
                    yield return sample;
                }
                else
                {
                    const int SLEEP_DURATION_MS = 10;
                    Thread.Sleep(SLEEP_DURATION_MS);
                }
            }
        }

        public void Dispose()
        {
            StopCapture();
            _capture?.Dispose();
        }
    }
}
