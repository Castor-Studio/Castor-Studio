using Dock.Model.Mvvm.Controls;

namespace CastorApplication.Docking;

// Empty marker subclasses: StudioViewModel backs four separate panes (preview, scene
// selector, status, stream controls), so the dockable's own type - not its Context - is
// what the DataTemplate in App.axaml keys off to pick the right view for each one.
public sealed class PreviewDocument : Document;

public sealed class SceneSelectorTool : Tool;

public sealed class StatusTool : Tool;

public sealed class StreamControlsTool : Tool;
