using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace CastorApplication.Converters;

// Drive orientation-adaptive pane layouts from a control's own rendered Bounds
// (bound via {Binding #Element.Bounds}), so panes docked into a narrow column
// switch to a stacked/vertical presentation instead of clipping horizontally.
public sealed class IsPortraitConverter : IValueConverter
{
    public static readonly IsPortraitConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Rect rect && rect.Height > rect.Width;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IsLandscapeConverter : IValueConverter
{
    public static readonly IsLandscapeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Rect rect && rect.Width >= rect.Height;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PortraitOrientationConverter : IValueConverter
{
    public static readonly PortraitOrientationConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Rect rect && rect.Height > rect.Width ? Orientation.Vertical : Orientation.Horizontal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
