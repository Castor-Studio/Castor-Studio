using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Controls;

/// <summary>
/// The menu of a source, the same whether opened on the picture or on its row in the list: a
/// source has one menu, wherever it is clicked. It opens on the source name, carries the
/// composition commands when the engine composes the source, then the list actions.
/// </summary>
/// <remarks>
/// Every command goes through the composition like a gesture: a refusal shows under the source
/// list, and the preview follows what the engine holds (see
/// <see cref="SceneCompositionViewModel.OverlayChanged"/>).
/// </remarks>
internal static class SourceMenu
{
    // The gap between a label and its keys: enough for the column to read on its own.
    private const double KeysGap = 32;

    /// <param name="source">The targeted source, as the list names it.</param>
    /// <param name="actions">
    /// What can be done from the list (rename, remove…), placed as the last group.
    /// </param>
    public static ContextMenu Create(
        SceneCompositionViewModel composition,
        SourceItemViewModel? source,
        IEnumerable<Control> actions)
    {
        var menu = new ContextMenu
        {
            Classes = { "composition-menu" },
            // Labels and keys share their width across rows: the keys line up in a column, each
            // one facing its command.
            [Grid.IsSharedSizeScopeProperty] = true
        };
        menu.Items.Add(Title(source));

        // A source the engine does not compose (hidden, audio) has nothing to place.
        if (composition.Selected is { } selected && selected.SourceId == source?.Id)
        {
            foreach (var item in CompositionItems(composition, selected)) menu.Items.Add(item);
        }

        var first = true;
        foreach (var action in actions)
        {
            if (first && menu.Items.Count > 1) menu.Items.Add(new Separator());
            first = false;
            menu.Items.Add(action);
        }

        return menu;
    }

    /// <summary>A row with no shortcut: its icon, its label, what it does.</summary>
    /// <param name="isChecked">
    /// For a toggle: when on, it shows a check mark where the other rows show their keys.
    /// </param>
    /// <param name="isDanger">An action that removes something: it reads in red.</param>
    public static MenuItem Item(
        string header,
        string icon,
        Action action,
        bool isEnabled = true,
        bool isChecked = false,
        bool isDanger = false)
    {
        var check = isChecked
            ? new TextBlock
            {
                Classes = { "menu-check" },
                Text = "✓",
                Margin = new Thickness(KeysGap, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            }
            : null;
        var item = new MenuItem
        {
            Icon = Icon(icon),
            Header = Row(new TextBlock { Text = header }, check),
            IsEnabled = isEnabled
        };
        if (isDanger) item.Classes.Add("danger");
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// A stroke icon, like those of the source list. Its color comes from the style carrying the
    /// <paramref name="style"/> class, which follows hover and held modes.
    /// </summary>
    public static Viewbox Icon(string data, string style = "menu-icon", double size = 16) => new()
    {
        Width = size,
        Height = size,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Canvas
        {
            Width = 24,
            Height = 24,
            Children = { new Avalonia.Controls.Shapes.Path { Classes = { style }, Data = Geometry.Parse(data) } }
        }
    };

    private static IEnumerable<Control> CompositionItems(SceneCompositionViewModel composition, SourceTransform selected)
    {
        // A held crop shows in the row that leaves it: it takes the accent color.
        var crop = Shortcut(
            composition,
            composition.IsCropMode ? "Terminer le rognage" : "Rogner",
            CompositionCommand.ToggleCrop);
        if (composition.IsCropMode) crop.Classes.Add("active");

        return
        [
            crop,
            Item("Réinitialiser le rognage", CompositionIcons.Restore, composition.ResetSelectedCrop, selected.Crop != SourceCrop.None),
            new Separator(),
            Shortcut(composition, "Pivoter de 90° à droite", CompositionCommand.RotateRight),
            Shortcut(composition, "Pivoter de 90° à gauche", CompositionCommand.RotateLeft),
            Item("Pivoter de 180°", CompositionIcons.RotateHalf, () => composition.RotateSelected(180)),
            Item("Réinitialiser la rotation", CompositionIcons.Restore, composition.ResetSelectedRotation, selected.Rotation != 0),
            new Separator(),
            Shortcut(composition, "Centrer dans le canvas", CompositionCommand.CenterSelected),
            Shortcut(composition, "Ajuster au canvas", CompositionCommand.FitSelected),
            Shortcut(composition, "Réinitialiser la transformation", CompositionCommand.ResetTransform),
            new Separator(),
            Shortcut(composition, "Mettre au premier plan", CompositionCommand.BringToFront),
            Shortcut(composition, "Rapprocher du premier plan", CompositionCommand.BringForward),
            Shortcut(composition, "Reculer vers l'arrière-plan", CompositionCommand.SendBackward),
            Shortcut(composition, "Mettre à l'arrière-plan", CompositionCommand.SendToBack),
            new Separator(),
            ZoomItem(composition, selected.Zoom.Factor),
            Shortcut(composition, "Réinitialiser le zoom", CompositionCommand.ResetZoom, selected.Zoom.IsZoomed)
        ];
    }

    // A row with a shortcut takes the same path as the key, and shows it opposite, drawn as
    // keycaps.
    private static MenuItem Shortcut(
        SceneCompositionViewModel composition,
        string header,
        CompositionCommand command,
        bool isEnabled = true)
    {
        var item = new MenuItem
        {
            Icon = Icon(CompositionIcons.Of(command)),
            Header = Row(new TextBlock { Text = header }, Keycaps(CompositionShortcuts.KeyTokensOf(command))),
            IsEnabled = isEnabled
        };
        item.Click += (_, _) => composition.Execute(new CompositionKeyAction(command));
        return item;
    }

    // The zoom slider lives in the menu, which stays open while it is dragged: each step goes
    // to the engine, and the preview shows the result at once.
    private static MenuItem ZoomItem(SceneCompositionViewModel composition, double factor)
    {
        // The factor takes the place of the keys: on the right, in their column.
        var value = new TextBlock
        {
            Text = FormatZoom(factor),
            Margin = new Thickness(KeysGap, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold
        };
        var slider = new Slider
        {
            Minimum = SourceZoom.MinFactor,
            Maximum = SourceZoom.MaxFactor,
            Value = factor,
            SmallChange = 0.1,
            LargeChange = 0.5,
            Width = 132,
            Margin = new Thickness(0, -10),
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.ValueChanged += (_, e) =>
        {
            composition.ZoomSelected(e.NewValue);
            value.Text = FormatZoom(composition.Selected?.Zoom.Factor ?? e.NewValue);
        };

        var item = new MenuItem
        {
            StaysOpenOnClick = true,
            Icon = Icon(CompositionIcons.Zoom),
            Header = Row(
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center },
                        slider
                    }
                },
                value)
        };
        // The menu teaches the direct gesture, faster once known.
        ToolTip.SetTip(item, "Sur l'aperçu : Alt + molette pour zoomer vers le pointeur, Alt + glisser pour déplacer l'image.");
        return item;
    }

    private static string FormatZoom(double factor) => $"×{factor:0.0}";

    // A menu row: the label, then what stands opposite it (the keys, the zoom factor). Both
    // columns share their width across the whole menu.
    private static Grid Row(Control label, Control? trailing)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "MenuLabel" },
                new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "MenuKeys" }
            },
            Children = { label }
        };
        if (trailing != null)
        {
            Grid.SetColumn(trailing, 1);
            row.Children.Add(trailing);
        }

        return row;
    }

    // Each key drawn as on a keyboard, right-aligned in their column.
    private static StackPanel Keycaps(IReadOnlyList<string> keys)
    {
        var caps = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            Margin = new Thickness(KeysGap, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var key in keys)
            caps.Children.Add(new Border { Classes = { "keycap" }, Child = new TextBlock { Text = key } });

        return caps;
    }

    // The source the menu targets, with the dot it has in the list: on overlapping sources,
    // this tells which one was picked.
    private static MenuItem Title(SourceItemViewModel? source)
    {
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = Color.TryParse(source?.Color, out var color) ? new SolidColorBrush(color) : null
        };

        return new MenuItem
        {
            Classes = { "menu-title" },
            IsHitTestVisible = false,
            Focusable = false,
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    dot,
                    new TextBlock
                    {
                        Text = source?.Name ?? "Source",
                        MaxWidth = 260,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
    }
}
