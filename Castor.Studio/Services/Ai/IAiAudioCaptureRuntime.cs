namespace CastorApplication.Services.Ai;

/// <summary>An audio source of a scene, used as the microphone of an AI camera.</summary>
internal sealed record AiMicrophone(Guid SceneId, Guid SourceId);

/// <summary>A scene of the AI session, with what the AI knows about it.</summary>
/// <param name="Role">Domain role (podcast: host, guest, guest2, wide, host_zoom). Empty: the worker decides.</param>
/// <param name="Microphone">The microphone of the person in this scene, if any.</param>
internal sealed record AiSceneSetup(
    Models.Studio.SceneDefinition Scene,
    string Role = "",
    AiMicrophone? Microphone = null);

/// <summary>One block of a microphone, mono float32, borrowed for the duration of the call.</summary>
internal delegate void AiAudioSink(int channel, ReadOnlySpan<float> mono, int sampleRate);

/// <summary>Captures the audio of single sources (not the program mix) for the local AI.</summary>
internal interface IAiAudioCaptureRuntime : IDisposable
{
    /// <summary>
    /// Captures each microphone to its channel, replacing any previous capture. A microphone that
    /// cannot be captured is skipped and named in the thrown exception, the others keep running.
    /// The sink runs on libobs audio threads and must not block.
    /// </summary>
    void Start(IReadOnlyDictionary<AiMicrophone, int> channels, AiAudioSink sink);

    void StopAll();
}

internal sealed class UnavailableAiAudioCaptureRuntime : IAiAudioCaptureRuntime
{
    public void Start(IReadOnlyDictionary<AiMicrophone, int> channels, AiAudioSink sink) =>
        throw new InvalidOperationException("La capture des micros pour l'IA nécessite le runtime LibObs.");
    public void StopAll() { }
    public void Dispose() { }
}
