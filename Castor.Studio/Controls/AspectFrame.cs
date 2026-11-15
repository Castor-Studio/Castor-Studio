using Avalonia;
using Avalonia.Controls;

namespace CastorApplication.Controls;

/// <summary>
/// Gives its child the largest size of the given proportions that fits, centered: what stands
/// in for the canvas when there is no picture to give it its shape.
/// </summary>
public sealed class AspectFrame : Decorator
{
    public static readonly StyledProperty<int> RatioWidthProperty =
        AvaloniaProperty.Register<AspectFrame, int>(nameof(RatioWidth), 16);

    public static readonly StyledProperty<int> RatioHeightProperty =
        AvaloniaProperty.Register<AspectFrame, int>(nameof(RatioHeight), 9);

    static AspectFrame() => AffectsMeasure<AspectFrame>(RatioWidthProperty, RatioHeightProperty);

    public int RatioWidth
    {
        get => GetValue(RatioWidthProperty);
        set => SetValue(RatioWidthProperty, value);
    }

    public int RatioHeight
    {
        get => GetValue(RatioHeightProperty);
        set => SetValue(RatioHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(Fit(availableSize));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = Fit(finalSize);
        Child?.Arrange(new Rect(
            (finalSize.Width - size.Width) / 2,
            (finalSize.Height - size.Height) / 2,
            size.Width,
            size.Height));
        return finalSize;
    }

    private Size Fit(Size area)
    {
        if (RatioWidth <= 0 || RatioHeight <= 0 || double.IsInfinity(area.Width) || double.IsInfinity(area.Height))
            return area;

        var scale = Math.Min(area.Width / RatioWidth, area.Height / RatioHeight);
        return new Size(RatioWidth * scale, RatioHeight * scale);
    }
}
