using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace CastorApplication.Controls;

/// <summary>What is missing, which decides the mascot shown.</summary>
public enum EmptyStateKind
{
    NoScene,
    NoSource
}

public partial class EmptyState : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Title), "");

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Description), "");

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<EmptyState, object?>(nameof(Action));

    public static readonly StyledProperty<double> MascotHeightProperty =
        AvaloniaProperty.Register<EmptyState, double>(nameof(MascotHeight), 180);

    public static readonly StyledProperty<EmptyStateKind> KindProperty =
        AvaloniaProperty.Register<EmptyState, EmptyStateKind>(nameof(Kind));

    // Une seule image par cas et par thème, partagée : ces écrans se montrent et se cachent souvent.
    private static readonly Dictionary<string, Bitmap> Mascots = [];

    public EmptyStateKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Le bouton qui remédie à ce qui manque, s'il y en a un.</summary>
    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public double MascotHeight
    {
        get => GetValue(MascotHeightProperty);
        set => SetValue(MascotHeightProperty, value);
    }

    public EmptyState()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => UpdateMascot();
        AttachedToVisualTree += (_, _) => UpdateMascot();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == KindProperty) UpdateMascot();
        };
    }

    private void UpdateMascot()
    {
        var useCase = Kind == EmptyStateKind.NoSource ? "no-source" : "no-scene";
        var theme = ActualThemeVariant == ThemeVariant.Light ? "light" : "dark";
        var name = $"mascot-{useCase}-{theme}.png";

        if (!Mascots.TryGetValue(name, out var mascot))
            Mascots[name] = mascot = new Bitmap(AssetLoader.Open(new Uri($"avares://Castor-Studio/Assets/Mascot/{name}")));

        Mascot.Source = mascot;
    }
}
