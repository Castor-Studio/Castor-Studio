using System.Globalization;
using System.Xml.Linq;

namespace Castor.Studio.Tests;

// Measures the palette in Styles/Colors.axaml against WCAG 2.x thresholds, so a value cannot
// drift below what the comments next to it promise. Each pair is a foreground and the
// background it actually sits on in the views; both themes go through the same list.
public sealed class ColorContrastTests
{
    private const double Text = 4.5;

    // Every surface a text brush is laid on: panels, cards, badges, hover and selection.
    private static readonly string[] TextBackgrounds =
    [
        "AppBg", "AppSurface", "AppSurface2", "AppSurface3", "AppBadgeBg",
        "AppNavHoverBg", "AppNavBadgeBg", "AppAccentBg", "AppAccentBg2"
    ];

    private static readonly string[] TextForegrounds =
    [
        "AppFg1", "AppFg2", "AppFg3", "AppFg4", "AppFg5", "AppFg6", "AppAccentFg",
        "StatusOkBrush", "StatusErrorBrush", "StatusWarningBrush"
    ];

    // Pairs outside the common surfaces: a status on its own tinted button or box, and text
    // on a filled status or accent.
    private static readonly (string Foreground, string Background, double Minimum)[] SpecificPairs =
    [
        ("StatusOkBrush", "AppLiveBg", Text),
        ("StatusErrorBrush", "AppStopBg", Text),
        ("StatusRecBrush", "AppRecBg", Text),
        ("StatusErrorBrush", "AppErrorBg", Text),
        ("AppOnStatusFg", "StatusOkBrush", Text),
        ("AppOnStatusFg", "StatusErrorBrush", Text),
        ("AppOnAccentFg", "AppAccentFg", Text),
    ];

    public static TheoryData<string> Themes => new() { "Dark", "Light" };

    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_text_brush_reaches_4_5_to_1_on_the_backgrounds_it_is_used_on(string theme)
    {
        var palette = LoadPalette(theme);
        var pairs = TextForegrounds
            .SelectMany(foreground => TextBackgrounds.Select(background => (foreground, background, Text)))
            .Concat(SpecificPairs);

        var failures = pairs
            .Select(pair => (pair, ratio: ContrastRatio(palette[pair.Item1], palette[pair.Item2])))
            .Where(result => result.ratio < result.pair.Item3)
            .Select(result => $"{result.pair.Item1} sur {result.pair.Item2} : {result.ratio:0.00}:1 < {result.pair.Item3}:1")
            .ToList();

        Assert.True(failures.Count == 0, $"{theme} :\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData("VideoFgBrush")]
    [InlineData("VideoFgMutedBrush")]
    [InlineData("VideoErrorBrush")]
    public void Text_over_video_reaches_4_5_to_1_on_black(string key)
    {
        var palette = LoadPalette("Dark");
        Assert.True(ContrastRatio(palette[key], "#000000") >= Text);
        Assert.True(ContrastRatio(palette[key], "#07070C") >= Text);
    }

    [Fact]
    public void The_text_scale_keeps_its_order_in_both_themes()
    {
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var palette = LoadPalette(theme);
            var ratios = new[] { "AppFg1", "AppFg2", "AppFg3", "AppFg4", "AppFg5", "AppFg6" }
                .Select(key => ContrastRatio(palette[key], palette["AppSurface"]))
                .ToArray();

            for (var step = 1; step < ratios.Length; step++)
                Assert.True(ratios[step] <= ratios[step - 1], $"{theme} : AppFg{step + 1} plus contrasté que AppFg{step}");
        }
    }

    [Fact]
    public void Contrast_ratio_matches_the_wcag_reference_values()
    {
        Assert.Equal(21.0, ContrastRatio("#000000", "#ffffff"), 2);
        Assert.Equal(1.0, ContrastRatio("#777777", "#777777"), 2);
        // The two figures quoted by the readability report for the former dark scale.
        Assert.Equal(1.71, ContrastRatio("#3c3c4e", "#13131c"), 2);
        Assert.Equal(2.84, ContrastRatio("#5c5c72", "#13131c"), 2);
    }

    // Theme-independent brushes first, then the theme's own, which win on a shared key.
    private static Dictionary<string, string> LoadPalette(string theme)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var root = XDocument.Load(ColorsPath()).Root!;

        static IEnumerable<XElement> Brushes(XElement dictionary) =>
            dictionary.Elements().Where(element => element.Name.LocalName == "SolidColorBrush");

        var palette = Brushes(root).ToDictionary(
            brush => (string)brush.Attribute(xaml + "Key")!,
            brush => (string)brush.Attribute("Color")!);

        var themeDictionary = root.Descendants()
            .Single(element => element.Name.LocalName == "ResourceDictionary"
                               && (string?)element.Attribute(xaml + "Key") == theme);
        foreach (var brush in Brushes(themeDictionary))
            palette[(string)brush.Attribute(xaml + "Key")!] = (string)brush.Attribute("Color")!;

        return palette;
    }

    private static string ColorsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Castor-Studio.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "Castor.Studio", "Styles", "Colors.axaml");
    }

    internal static double ContrastRatio(string first, string second)
    {
        var (lighter, darker) = (RelativeLuminance(first), RelativeLuminance(second)) switch
        {
            var (a, b) when a >= b => (a, b),
            var (a, b) => (b, a)
        };
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var digits = hex.TrimStart('#');
        digits = digits[^6..]; // drop an alpha channel: only opaque colors are measured

        double Channel(int index)
        {
            var value = int.Parse(digits.Substring(index, 2), NumberStyles.HexNumber) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }
}
