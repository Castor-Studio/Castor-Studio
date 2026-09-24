using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

public sealed class SourceListViewTests
{
    [Fact]
    public void Scene_order_is_kept_as_is()
    {
        var sources = Sources(("Caméra", SourceKind.Video), ("Micro", SourceKind.Audio), ("Clip", SourceKind.Media));

        var displayed = SourceListView.Apply(sources, SourceListSort.SceneOrder, SourceListFilter.All);

        Assert.Equal(sources, displayed);
    }

    [Fact]
    public void Sorts_by_name_and_by_kind_keeping_scene_order_for_ties()
    {
        var sources = Sources(("b-micro", SourceKind.Audio), ("c-clip", SourceKind.Media),
            ("A-caméra", SourceKind.Video), ("d-écran", SourceKind.Video));

        Assert.Equal(["A-caméra", "b-micro", "c-clip", "d-écran"],
            Names(SourceListView.Apply(sources, SourceListSort.NameAscending, SourceListFilter.All)));
        Assert.Equal(["d-écran", "c-clip", "b-micro", "A-caméra"],
            Names(SourceListView.Apply(sources, SourceListSort.NameDescending, SourceListFilter.All)));
        Assert.Equal(["A-caméra", "d-écran", "c-clip", "b-micro"],
            Names(SourceListView.Apply(sources, SourceListSort.Kind, SourceListFilter.All)));
    }

    [Fact]
    public void Filters_by_kind()
    {
        var sources = Sources(("Caméra", SourceKind.Video), ("Micro", SourceKind.Audio), ("Écran", SourceKind.Video));

        Assert.Equal(["Caméra", "Écran"],
            Names(SourceListView.Apply(sources, SourceListSort.SceneOrder, SourceListFilter.Video)));
        Assert.Empty(SourceListView.Apply(sources, SourceListSort.SceneOrder, SourceListFilter.Media));
    }

    [Fact]
    public void Icon_and_label_follow_the_captured_device()
    {
        SourceItemViewModel Item(SourceKind kind, VideoCaptureKind? video = null, AudioCaptureKind? audio = null) =>
            new(new SourceDefinition { Kind = kind, VideoCaptureKind = video, AudioCaptureKind = audio });

        Assert.Equal(SourceIcons.Camera, Item(SourceKind.Video, VideoCaptureKind.Camera).IconPath);
        Assert.Equal("Caméra", Item(SourceKind.Video, VideoCaptureKind.Camera).Type);
        Assert.Equal(SourceIcons.Monitor, Item(SourceKind.Video, VideoCaptureKind.Monitor).IconPath);
        Assert.Equal(SourceIcons.Window, Item(SourceKind.Video, VideoCaptureKind.Window).IconPath);
        Assert.Equal(SourceIcons.Mic, Item(SourceKind.Audio, audio: AudioCaptureKind.Microphone).IconPath);
        Assert.Equal(SourceIcons.Volume, Item(SourceKind.Audio, audio: AudioCaptureKind.LoopbackGlobal).IconPath);
        Assert.Equal(SourceIcons.Movie, Item(SourceKind.Media).IconPath);
        // Saved before the capture kind was recorded: generic icon and label of the kind.
        Assert.Equal(SourceIcons.Camera, Item(SourceKind.Video).IconPath);
        Assert.Equal("Vidéo", Item(SourceKind.Video).Type);
    }

    private static List<SourceItemViewModel> Sources(params (string Name, SourceKind Kind)[] sources) =>
        sources.Select(source => new SourceItemViewModel(new SourceDefinition { Name = source.Name, Kind = source.Kind })).ToList();

    private static string[] Names(IEnumerable<SourceItemViewModel> sources) => sources.Select(source => source.Name).ToArray();
}
