using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

public sealed class StudioItemViewModelTests
{
    [Fact]
    public void File_source_exposes_observable_ui_state_and_round_trips()
    {
        var source = new SourceItemViewModel(new SourceDefinition
        {
            Name = "clip.mp4",
            Kind = SourceKind.Video,
            Origin = SourceOrigin.File,
            OriginPath = "C:\\media\\clip.mp4",
            Loop = true
        });
        var changed = new List<string?>();
        source.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        source.Loop = false;

        Assert.True(source.IsFileSource);
        Assert.Equal("Vidéo", source.Type);
        Assert.Contains(nameof(SourceItemViewModel.Loop), changed);
        Assert.False(source.ToDefinition().Loop);
    }

    [Fact]
    public void Scene_ui_selection_is_not_persisted_in_the_definition()
    {
        var scene = new SceneItemViewModel(new SceneDefinition { Name = "Scène" })
        {
            IsMultiSelected = true,
            IsActive = true
        };

        var definition = scene.ToDefinition();

        Assert.Equal("Scène", definition.Name);
        Assert.DoesNotContain("Selected", definition.GetType().GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("Active", definition.GetType().GetProperties().Select(property => property.Name));
    }
}
