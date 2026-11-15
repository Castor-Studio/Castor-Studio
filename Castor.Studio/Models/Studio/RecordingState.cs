namespace CastorApplication.Models.Studio;

// Allowed moves: Idle → Recording, Recording ↔ Paused, Recording / Paused → Stopping,
// Stopping → Idle. StudioWorkspaceViewModel ignores anything else.
public enum RecordingState
{
    Idle,
    Recording,
    Paused,
    Stopping
}
