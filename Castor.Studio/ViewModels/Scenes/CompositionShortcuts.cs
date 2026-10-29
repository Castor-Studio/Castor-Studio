using Avalonia.Input;

namespace CastorApplication.ViewModels.Scenes;

/// <summary>Ce qu'une touche, un bouton ou une ligne du menu demande au canvas.</summary>
public enum CompositionCommand
{
    SelectNext,
    SelectPrevious,
    Nudge,
    CenterSelected,
    FitSelected,
    ResetTransform,
    RotateRight,
    RotateLeft,
    ToggleCrop,
    ZoomIn,
    ZoomOut,
    ResetZoom,
    BringForward,
    SendBackward,
    BringToFront,
    SendToBack
}

/// <summary>
/// Une touche résolue : la commande, et pour <see cref="CompositionCommand.Nudge"/> le pas
/// en pixels du canvas sur chaque axe.
/// </summary>
public readonly record struct CompositionKeyAction(CompositionCommand Command, int StepX = 0, int StepY = 0);

/// <summary>Un raccourci tel qu'on l'enseigne : ses touches, en clair, et ce qu'il fait.</summary>
public sealed record CompositionShortcut(string Keys, string Label);

/// <summary>Un groupe de raccourcis, sous un titre, pour l'aide de la barre du canvas.</summary>
public sealed record CompositionShortcutGroup(string Title, IReadOnlyList<CompositionShortcut> Shortcuts);

/// <summary>
/// Les raccourcis clavier du canvas de composition, écrits à un seul endroit : la touche
/// qui déclenche une commande est celle que le menu, les bulles des boutons et l'aide
/// affichent. Les combinaisons reprennent celles d'OBS là où elles existent (Ctrl+D, Ctrl+F,
/// Ctrl+R) : un opérateur qui vient d'OBS n'a rien à réapprendre.
/// </summary>
/// <remarks>
/// Ces touches ne s'écoutent que sur l'aperçu, quand il a le clavier : une lettre seule
/// (R, C) ne vole donc rien à un champ texte.
/// </remarks>
public static class CompositionShortcuts
{
    // Un pas de flèche, et un pas avec Maj : le pixel près, puis de quoi traverser le canvas
    // sans y passer la journée.
    public const int NudgeStep = 1;
    public const int LargeNudgeStep = 10;

    private sealed record Binding(CompositionCommand Command, Key Key, KeyModifiers Modifiers, string Keys);

    private static readonly Binding[] Bindings =
    [
        new(CompositionCommand.SelectNext, Key.Tab, KeyModifiers.None, "Tab"),
        new(CompositionCommand.SelectPrevious, Key.Tab, KeyModifiers.Shift, "Maj+Tab"),
        new(CompositionCommand.CenterSelected, Key.D, KeyModifiers.Control, "Ctrl+D"),
        new(CompositionCommand.FitSelected, Key.F, KeyModifiers.Control, "Ctrl+F"),
        new(CompositionCommand.ResetTransform, Key.R, KeyModifiers.Control, "Ctrl+R"),
        new(CompositionCommand.RotateRight, Key.R, KeyModifiers.None, "R"),
        new(CompositionCommand.RotateLeft, Key.R, KeyModifiers.Shift, "Maj+R"),
        new(CompositionCommand.ToggleCrop, Key.C, KeyModifiers.None, "C"),
        new(CompositionCommand.BringForward, Key.PageUp, KeyModifiers.None, "Page↑"),
        new(CompositionCommand.SendBackward, Key.PageDown, KeyModifiers.None, "Page↓"),
        new(CompositionCommand.BringToFront, Key.PageUp, KeyModifiers.Control, "Ctrl+Page↑"),
        new(CompositionCommand.SendToBack, Key.PageDown, KeyModifiers.Control, "Ctrl+Page↓")
    ];

    /// <summary>
    /// La commande qu'une touche demande, ou rien. Alt ne compte pas : il tient le rognage,
    /// et une commande doit répondre pareil qu'on rogne ou non. Pour le zoom, c'est le
    /// caractère tapé qui compte, pas la touche : « + » n'est pas au même endroit sur tous
    /// les claviers.
    /// </summary>
    public static CompositionKeyAction? Resolve(Key key, KeyModifiers modifiers, string? symbol = null)
    {
        var held = modifiers & ~KeyModifiers.Alt;

        if (key is Key.Left or Key.Right or Key.Up or Key.Down && (held & ~KeyModifiers.Shift) == 0)
        {
            var step = held.HasFlag(KeyModifiers.Shift) ? LargeNudgeStep : NudgeStep;
            return new CompositionKeyAction(
                CompositionCommand.Nudge,
                key switch { Key.Left => -step, Key.Right => step, _ => 0 },
                key switch { Key.Up => -step, Key.Down => step, _ => 0 });
        }

        foreach (var binding in Bindings)
        {
            if (binding.Key == key && binding.Modifiers == held) return new CompositionKeyAction(binding.Command);
        }

        // Le zoom ne demande aucun modificateur, mais Maj peut être nécessaire pour taper « + ».
        if ((held & ~KeyModifiers.Shift) != 0) return null;

        if (key is Key.Add or Key.OemPlus || symbol == "+") return new CompositionKeyAction(CompositionCommand.ZoomIn);
        if (key is Key.Subtract or Key.OemMinus || symbol == "-") return new CompositionKeyAction(CompositionCommand.ZoomOut);
        if (key == Key.NumPad0 || symbol == "0") return new CompositionKeyAction(CompositionCommand.ResetZoom);

        return null;
    }

    /// <summary>Les touches d'une commande, en clair, telles que les affichent menu et bulles.</summary>
    public static string KeysOf(CompositionCommand command) => command switch
    {
        CompositionCommand.ZoomIn => "+",
        CompositionCommand.ZoomOut => "−",
        CompositionCommand.ResetZoom => "0",
        CompositionCommand.Nudge => "Flèches",
        _ => Bindings.First(binding => binding.Command == command).Keys
    };

    /// <summary>Une bulle de bouton : ce qu'il fait, puis la touche qui fait la même chose.</summary>
    public static string Describe(string label, CompositionCommand command) => $"{label} ({KeysOf(command)})";

    /// <summary>L'aide complète, par groupe, telle que la barre du canvas l'affiche.</summary>
    public static IReadOnlyList<CompositionShortcutGroup> Help { get; } =
    [
        new("Choisir",
        [
            new("Clic", "Choisir la source sous le pointeur"),
            new($"{KeysOf(CompositionCommand.SelectNext)} / {KeysOf(CompositionCommand.SelectPrevious)}", "Source suivante / précédente"),
            new("Échap", "Annuler le geste, quitter le rognage, puis ne plus rien choisir")
        ]),
        new("Placer",
        [
            new("Flèches", $"Déplacer de {NudgeStep} px"),
            new("Maj+Flèches", $"Déplacer de {LargeNudgeStep} px"),
            new(KeysOf(CompositionCommand.CenterSelected), "Centrer dans le canvas"),
            new(KeysOf(CompositionCommand.FitSelected), "Ajuster au canvas"),
            new(KeysOf(CompositionCommand.ResetTransform), "Réinitialiser la transformation"),
            new("Ctrl (en glissant)", "Couper l'aimant")
        ]),
        new("Transformer",
        [
            new($"{KeysOf(CompositionCommand.RotateRight)} / {KeysOf(CompositionCommand.RotateLeft)}", "Pivoter de 90° à droite / à gauche"),
            new("Maj (en tournant)", "Tourner par pas de 15°"),
            new("Maj (sur un angle)", "Libérer les proportions"),
            new(KeysOf(CompositionCommand.ToggleCrop), "Rogner / terminer le rognage"),
            new("Alt (maintenu)", "Rogner le temps de la touche"),
            new("Entrée", "Terminer le rognage")
        ]),
        new("Zoomer dans l'image",
        [
            new("+ / −", "Zoomer / dézoomer"),
            new("0", "Image entière"),
            new("Alt+Flèches", "Faire glisser l'image zoomée"),
            new("Alt+Molette", "Zoomer vers le pointeur")
        ]),
        new("Empiler",
        [
            new($"{KeysOf(CompositionCommand.BringForward)} / {KeysOf(CompositionCommand.SendBackward)}", "Rapprocher / reculer d'un plan"),
            new($"{KeysOf(CompositionCommand.BringToFront)} / {KeysOf(CompositionCommand.SendToBack)}", "Premier plan / arrière-plan")
        ])
    ];
}
