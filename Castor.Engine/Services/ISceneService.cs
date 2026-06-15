using System.Collections.ObjectModel;
using Castor.Engine.Models;

namespace Castor.Engine.Services;

public interface ISceneService
{
    ObservableCollection<SceneItem> Scenes { get; }
    SceneItem? ActiveScene { get; }
    SceneItem CreateScene(string name);
    void DeleteScene(SceneItem scene);
    void SetActiveScene(SceneItem scene);
    SourceItem AddVideoSource(SceneItem scene, CaptureSourceOption source);
    SourceItem AddVideoSource(SceneItem scene, string label, string url);
    SourceItem AddAudioSource(SceneItem scene, AudioSourceOption source);
    void RemoveSource(SceneItem scene, SourceItem source);
}
