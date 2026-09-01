using Dock.Model.Mvvm.Controls;

namespace CastorApplication.Docking;

// Empty marker subclasses: StudioViewModel backs three separate panes (preview, scene
// selector, controls), so the dockable's own type - not its Context - is what the
// DataTemplate in App.axaml keys off to pick the right view for each one.
public sealed class PreviewDocument : Document;

public sealed class SceneSelectorTool : Tool;

public sealed class ControlsTool : Tool;
