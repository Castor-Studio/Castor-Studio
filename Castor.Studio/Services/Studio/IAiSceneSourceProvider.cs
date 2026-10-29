using LibObs;

namespace CastorApplication.Services.Studio;

/// <summary>What the local AI reads from the engine: the picture of a scene, the audio of a source.</summary>
internal interface IAiSceneSourceProvider
{
    bool IsAvailable { get; }

    /// <summary>A strong reference to a scene source, to render it apart.</summary>
    ObsSource? AcquireSceneSource(Guid sceneId);

    /// <summary>Hooks the audio of one source; null when the source does not exist.</summary>
    ObsSourceAudioCapture? AddSourceAudioCapture(Guid sceneId, Guid sourceId, ObsSourceAudioCallback callback);
}
