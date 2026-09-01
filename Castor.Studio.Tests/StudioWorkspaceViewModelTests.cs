using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class StudioWorkspaceViewModelTests
{
    [Fact]
    public void Active_scene_is_unique_and_falls_back_after_deletion()
    {
        var workspace = new StudioWorkspaceViewModel();
        var first = workspace.CreateScene("Première");
        var second = workspace.CreateScene("Deuxième");

        workspace.SelectScene(second);

        Assert.Same(second, workspace.ActiveScene);
        Assert.True(second.IsActive);
        Assert.False(first.IsActive);

        workspace.DeleteScene(second);

        Assert.Same(first, workspace.ActiveScene);
        Assert.True(first.IsActive);
    }

    [Fact]
    public void Adding_a_source_replaces_only_the_same_media_kind()
    {
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Scène");

        workspace.AddSource(scene, FileSource("video-1.mp4", SourceKind.Video));
        workspace.AddSource(scene, FileSource("audio.wav", SourceKind.Audio));
        workspace.AddSource(scene, FileSource("video-2.mp4", SourceKind.Video));

        Assert.Equal(2, scene.Sources.Count);
        Assert.Contains(scene.Sources, source => source.Name == "video-2.mp4");
        Assert.Contains(scene.Sources, source => source.Name == "audio.wav");
    }

    private static SourceDefinition FileSource(string name, SourceKind kind) => new()
    {
        Name = name,
        Kind = kind,
        Origin = SourceOrigin.File,
        OriginPath = name,
        OriginLabel = name
    };
}
