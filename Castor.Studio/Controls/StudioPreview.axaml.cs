using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Controls;

public partial class StudioPreview : UserControl
{
    public static readonly StyledProperty<SceneItemViewModel?> SceneProperty =
        AvaloniaProperty.Register<StudioPreview, SceneItemViewModel?>(nameof(Scene));

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<StudioPreview, string>(nameof(Message), "LibObs n'est pas encore connecté.");

    public static readonly StyledProperty<IScenePreviewRuntime?> PreviewRuntimeProperty =
        AvaloniaProperty.Register<StudioPreview, IScenePreviewRuntime?>(nameof(PreviewRuntime));

    /// <summary>
    /// Set it to have the engine outline every composed source on the picture. Left unset,
    /// the preview shows the picture alone.
    /// </summary>
    public static readonly StyledProperty<SceneCompositionViewModel?> CompositionProperty =
        AvaloniaProperty.Register<StudioPreview, SceneCompositionViewModel?>(nameof(Composition));

    public static readonly StyledProperty<int> BaseCanvasWidthProperty =
        AvaloniaProperty.Register<StudioPreview, int>(nameof(BaseCanvasWidth), 1920);

    public static readonly StyledProperty<int> BaseCanvasHeightProperty =
        AvaloniaProperty.Register<StudioPreview, int>(nameof(BaseCanvasHeight), 1080);

    /// <summary>
    /// Whether the scene name is drawn over the picture. Off where the host already
    /// names the scene, so the name is not written twice on the same tile.
    /// </summary>
    public static readonly StyledProperty<bool> ShowSceneNameProperty =
        AvaloniaProperty.Register<StudioPreview, bool>(nameof(ShowSceneName), true);

    public bool ShowSceneName
    {
        get => GetValue(ShowSceneNameProperty);
        set => SetValue(ShowSceneNameProperty, value);
    }

    public SceneItemViewModel? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public IScenePreviewRuntime? PreviewRuntime
    {
        get => GetValue(PreviewRuntimeProperty);
        set => SetValue(PreviewRuntimeProperty, value);
    }

    public SceneCompositionViewModel? Composition
    {
        get => GetValue(CompositionProperty);
        set => SetValue(CompositionProperty, value);
    }

    public int BaseCanvasWidth
    {
        get => GetValue(BaseCanvasWidthProperty);
        set => SetValue(BaseCanvasWidthProperty, value);
    }

    public int BaseCanvasHeight
    {
        get => GetValue(BaseCanvasHeightProperty);
        set => SetValue(BaseCanvasHeightProperty, value);
    }

    public StudioPreview()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdatePreviewViewport();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == BaseCanvasWidthProperty || change.Property == BaseCanvasHeightProperty)
                UpdatePreviewViewport();
        };
        UpdatePreviewViewport();
    }

    /// <summary>
    /// Ramène le clic dans le repère du canvas et laisse la composition choisir la source
    /// visée. Sans composition, la vue ne fait que montrer : un clic n'y sélectionne rien.
    /// </summary>
    private void OnPicturePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var composition = Composition;
        if (composition == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var picture = NativePreview.Bounds;
        if (picture.Width <= 0 || picture.Height <= 0 || BaseCanvasWidth <= 0 || BaseCanvasHeight <= 0) return;

        var point = e.GetPosition(this);
        if (picture.Contains(point))
        {
            // L'image occupe exactement le rectangle de la surface native : un point de
            // l'écran s'y ramène par le seul rapport des deux tailles. C'est le changement
            // de repère du moteur, pris à l'envers, et le seul endroit où il est écrit.
            composition.SelectAt(
                (point.X - picture.X) * BaseCanvasWidth / picture.Width,
                (point.Y - picture.Y) * BaseCanvasHeight / picture.Height);
        }
        else
        {
            // Cliquer à côté de l'image, c'est ne viser aucune source.
            composition.ClearSelection();
        }

        NativePreview.RefreshComposition();
        e.Handled = true;
    }

    private void UpdatePreviewViewport()
    {
        if (BaseCanvasWidth <= 0 || BaseCanvasHeight <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var scale = Math.Min(Bounds.Width / BaseCanvasWidth, Bounds.Height / BaseCanvasHeight);
        NativePreview.Width = Math.Max(1, Math.Round(BaseCanvasWidth * scale));
        NativePreview.Height = Math.Max(1, Math.Round(BaseCanvasHeight * scale));
    }
}
