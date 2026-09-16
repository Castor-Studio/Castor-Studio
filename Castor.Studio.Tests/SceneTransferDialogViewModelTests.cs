using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

public sealed class SceneTransferDialogViewModelTests
{
    [Fact]
    public void Import_checks_everything_and_flags_collisions_before_writing()
    {
        var existing = new SceneItemViewModel(new SceneDefinition { Name = "Plateau" });
        var dialog = SceneTransferDialogViewModel.ForImport(
            [existing],
            [
                new SceneTransferItem(new SceneDefinition { Name = "plateau " }),
                new SceneTransferItem(new SceneDefinition { Id = existing.Id, Name = "Ancien plateau" }),
                new SceneTransferItem(new SceneDefinition { Name = "Interview" }),
                new SceneTransferItem(new SceneDefinition { Name = "Interview" }),
                new SceneTransferItem(new SceneDefinition { Name = "Générique" }),
            ]);

        Assert.All(dialog.Items, item => Assert.True(item.IsChecked));
        Assert.Contains("porte déjà ce nom", dialog.Items[0].Collision);
        Assert.Contains("copie", dialog.Items[1].Collision);
        Assert.Contains("plusieurs fois", dialog.Items[2].Collision);
        Assert.Contains("plusieurs fois", dialog.Items[3].Collision);
        Assert.False(dialog.Items[4].HasCollision);
        Assert.Equal("5 scènes sur 5 seront importées.", dialog.Summary);

        // Unchecking one of the duplicates clears the flag on the other.
        dialog.Items[3].IsChecked = false;

        Assert.False(dialog.Items[2].HasCollision);
        Assert.Equal("4 scènes sur 5 seront importées.", dialog.Summary);
    }

    [Fact]
    public void Export_pre_checks_the_selection_or_every_scene_without_one()
    {
        var plateau = new SceneItemViewModel(new SceneDefinition { Name = "Plateau" });
        var interview = new SceneItemViewModel(new SceneDefinition { Name = "Interview" });

        var all = SceneTransferDialogViewModel.ForExport([plateau, interview]);
        Assert.Equal([true, true], all.Items.Select(item => item.IsChecked));

        interview.IsMultiSelected = true;
        var selection = SceneTransferDialogViewModel.ForExport([plateau, interview]);
        Assert.Equal([false, true], selection.Items.Select(item => item.IsChecked));
        Assert.Equal("1 scène sur 2 sera exportée.", selection.Summary);
        Assert.All(selection.Items, item => Assert.False(item.HasCollision));
    }

    [Fact]
    public void Nothing_checked_cannot_be_confirmed_and_cancel_reports_it()
    {
        var dialog = SceneTransferDialogViewModel.ForExport([new SceneItemViewModel(new SceneDefinition { Name = "Plateau" })]);
        bool? closed = null;
        dialog.CloseRequested += confirmed => closed = confirmed;

        dialog.ToggleAllCommand.Execute(null);
        Assert.False(dialog.CanConfirm);
        dialog.ConfirmCommand.Execute(null);
        Assert.Null(closed);

        dialog.ToggleAllCommand.Execute(null);
        Assert.True(dialog.AreAllChecked);

        dialog.CancelCommand.Execute(null);
        Assert.False(closed);
    }
}
