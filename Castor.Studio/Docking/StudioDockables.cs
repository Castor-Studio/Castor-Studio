using Dock.Model.Mvvm.Controls;

namespace CastorApplication.Docking;

// Empty marker subclasses: StudioViewModel backs four separate panes (preview, scene
// selector, status, stream controls), so the dockable's own type - not its Context - is
// what the DataTemplate in App.axaml keys off to pick the right view for each one.
public sealed class PreviewTool : Tool;

public sealed class SceneSelectorTool : Tool;

public sealed class StatusTool : Tool;

public sealed class StreamControlsTool : Tool;

// The preview used to be a document, with a tab instead of the bar the other panels have. Kept
// only so a layout saved back then still loads; StudioDockFactory.MigrateLegacyPreview turns it
// into a PreviewTool straight away.
public sealed class PreviewDocument : Document;
