using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace CastorApplication.Controls;

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

    // Une seule image par thème, partagée : ces écrans se montrent et se cachent souvent.
    private static Bitmap? _darkMascot;
    private static Bitmap? _lightMascot;

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
    }

    private void UpdateMascot() =>
        Mascot.Source = ActualThemeVariant == ThemeVariant.Light
            ? _lightMascot ??= Load("mascot.png")
            : _darkMascot ??= Load("mascot-dark.png");

    private static Bitmap Load(string name) =>
        new(AssetLoader.Open(new Uri($"avares://Castor-Studio/Assets/{name}")));
}
