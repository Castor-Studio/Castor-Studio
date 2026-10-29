using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Controls;

/// <summary>
/// Le menu d'une source, le même qu'on l'ouvre sur l'image ou sur sa ligne dans la liste :
/// une source n'a qu'un menu, où qu'on clique. Il s'ouvre sur son nom, porte ses commandes
/// de composition quand le moteur la compose, puis ce qui se fait depuis la liste.
/// </summary>
/// <remarks>
/// Chaque commande passe par la composition comme un geste : un refus se dit sous la liste
/// des sources, et l'aperçu suit ce que le moteur détient (voir
/// <see cref="SceneCompositionViewModel.OverlayChanged"/>).
/// </remarks>
internal static class SourceMenu
{
    // L'écart entre un libellé et ses touches : assez pour que la colonne se lise à part.
    private const double KeysGap = 32;

    /// <param name="source">La source visée, telle que la liste la nomme.</param>
    /// <param name="actions">
    /// Ce qui se fait depuis la liste (renommer, retirer…), posé en dernier groupe.
    /// </param>
    public static ContextMenu Create(
        SceneCompositionViewModel composition,
        SourceItemViewModel? source,
        IEnumerable<Control> actions)
    {
        var menu = new ContextMenu
        {
            Classes = { "composition-menu" },
            // Libellés et touches partagent leur largeur d'une ligne à l'autre : les touches
            // tombent en colonne, chacune en face de sa commande.
            [Grid.IsSharedSizeScopeProperty] = true
        };
        menu.Items.Add(Title(source));

        // Une source que le moteur ne compose pas (masquée, audio) n'a rien à placer.
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

    /// <summary>Une ligne sans raccourci : son icône, son libellé, ce qu'elle fait.</summary>
    /// <param name="isChecked">
    /// Pour une bascule : tenue, elle porte une coche en face, là où les autres lignes ont
    /// leurs touches.
    /// </param>
    /// <param name="isDanger">Une action qui retire quelque chose : elle se lit en rouge.</param>
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
    /// Une icône au trait, comme celles de la liste des sources ; sa couleur vient du style
    /// qui porte la classe <paramref name="style"/>, qui la suit au survol et dans un mode tenu.
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
        // Le rognage tenu se dit dans la ligne qui en sort : elle passe à l'accent.
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

    // Une ligne qui a son raccourci passe par le même chemin que la touche, et la montre en
    // face, dessinée en touches de clavier.
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

    // Le curseur du zoom vit dans le menu, qui reste ouvert pendant qu'on le tire : chaque
    // cran part au moteur, et l'aperçu montre aussitôt ce qu'il en a fait.
    private static MenuItem ZoomItem(SceneCompositionViewModel composition, double factor)
    {
        // Le facteur prend la place des touches : à droite, en colonne avec elles.
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
        // Le menu enseigne le geste direct, plus rapide une fois connu.
        ToolTip.SetTip(item, "Sur l'aperçu : Alt + molette pour zoomer vers le pointeur, Alt + glisser pour déplacer l'image.");
        return item;
    }

    private static string FormatZoom(double factor) => $"×{factor:0.0}";

    // Une ligne du menu : le libellé, puis ce qui se tient en face (les touches, le facteur
    // du zoom). Les deux colonnes partagent leur largeur dans tout le menu.
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

    // Chaque touche dessinée comme sur le clavier, alignées à droite de leur colonne.
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

    // La source que vise le menu, sous la pastille qu'elle porte dans la liste : sur des
    // sources qui se chevauchent, c'est ce qui dit laquelle a été prise.
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
