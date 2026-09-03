using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace CastorApplication.Docking;

// The strip a panel is held by: its chrome bar, or its tab and the empty room next to it in a
// tab strip. Double-clicking it detaches the panel in the main window, and sends it back from a
// detached one, so both windows have to agree on what counts as the bar.
internal static class StudioPanelBar
{
    public static IDockable? Find(object? source)
    {
        if (source is not Visual visual)
        {
            return null;
        }

        // The buttons sitting on the bar keep their own single-click behaviour.
        if (visual.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return null;
        }

        // includeSelf, because clicking the empty room next to the tabs reports the strip itself.
        var bar = (Visual?)visual.FindAncestorOfType<ToolChromeControl>(includeSelf: true)
                  ?? visual.FindAncestorOfType<DocumentTabStripItem>(includeSelf: true)
                  ?? (Visual?)visual.FindAncestorOfType<DocumentTabStrip>(includeSelf: true)
                  ?? visual.FindAncestorOfType<ToolTabStrip>(includeSelf: true);

        return bar?.DataContext as IDockable;
    }
}
