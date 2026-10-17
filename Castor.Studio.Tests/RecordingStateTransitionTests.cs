using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class RecordingStateTransitionTests
{
    [Theory]
    [InlineData(RecordingState.Idle, RecordingState.Recording)]
    [InlineData(RecordingState.Recording, RecordingState.Paused)]
    [InlineData(RecordingState.Paused, RecordingState.Recording)]
    [InlineData(RecordingState.Recording, RecordingState.Stopping)]
    [InlineData(RecordingState.Paused, RecordingState.Stopping)]
    [InlineData(RecordingState.Stopping, RecordingState.Idle)]
    public void Allowed_transitions_are_applied(RecordingState from, RecordingState to)
    {
        var workspace = WorkspaceIn(from);

        Assert.True(workspace.TrySetRecordingState(to));
        Assert.Equal(to, workspace.RecordingState);
    }

    [Theory]
    [InlineData(RecordingState.Idle, RecordingState.Paused)]
    [InlineData(RecordingState.Idle, RecordingState.Stopping)]
    [InlineData(RecordingState.Recording, RecordingState.Idle)]
    [InlineData(RecordingState.Paused, RecordingState.Idle)]
    [InlineData(RecordingState.Stopping, RecordingState.Recording)]
    [InlineData(RecordingState.Stopping, RecordingState.Paused)]
    public void Invalid_transitions_leave_the_state_untouched(RecordingState from, RecordingState to)
    {
        var workspace = WorkspaceIn(from);
        var changes = new List<string?>();
        workspace.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.False(workspace.TrySetRecordingState(to));
        Assert.Equal(from, workspace.RecordingState);
        Assert.Empty(changes);
    }

    [Fact]
    public void Re_applying_the_current_state_is_a_silent_no_op()
    {
        var workspace = WorkspaceIn(RecordingState.Recording);
        var changes = new List<string?>();
        workspace.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.True(workspace.TrySetRecordingState(RecordingState.Recording));
        Assert.Empty(changes);
    }

    [Theory]
    [InlineData(RecordingState.Idle, false)]
    [InlineData(RecordingState.Recording, true)]
    [InlineData(RecordingState.Paused, true)]
    [InlineData(RecordingState.Stopping, true)]
    public void IsRecording_is_derived_from_the_state(RecordingState state, bool expected)
    {
        Assert.Equal(expected, WorkspaceIn(state).IsRecording);
    }

    [Fact]
    public void A_state_change_notifies_both_the_state_and_IsRecording()
    {
        var workspace = new StudioWorkspaceViewModel();
        var changes = new List<string?>();
        workspace.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        workspace.TrySetRecordingState(RecordingState.Recording);

        Assert.Equal(
            [nameof(StudioWorkspaceViewModel.RecordingState), nameof(StudioWorkspaceViewModel.IsRecording)],
            changes);
    }

    private static StudioWorkspaceViewModel WorkspaceIn(RecordingState state)
    {
        var workspace = new StudioWorkspaceViewModel();
        RecordingState[] path = state switch
        {
            RecordingState.Idle => [],
            RecordingState.Recording => [RecordingState.Recording],
            RecordingState.Paused => [RecordingState.Recording, RecordingState.Paused],
            RecordingState.Stopping => [RecordingState.Recording, RecordingState.Stopping],
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        foreach (var step in path) Assert.True(workspace.TrySetRecordingState(step));
        return workspace;
    }
}
