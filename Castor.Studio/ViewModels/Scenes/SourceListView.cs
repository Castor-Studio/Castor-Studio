using CastorApplication.Models.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Scenes;

public enum SourceListSort
{
    SceneOrder,
    NameAscending,
    NameDescending,
    Kind
}

public enum SourceListFilter
{
    All,
    Video,
    Audio,
    Media
}

// One entry of the sources sort/filter flyout. Exactly one of Sort / Filter is set.
public sealed partial class SourceListOption : ObservableObject
{
    public string Label { get; }
    public SourceListSort? Sort { get; }
    public SourceListFilter? Filter { get; }

    [ObservableProperty] private bool _isSelected;

    private SourceListOption(string label, SourceListSort? sort, SourceListFilter? filter)
    {
        Label = label;
        Sort = sort;
        Filter = filter;
    }

    public static SourceListOption ForSort(string label, SourceListSort sort) => new(label, sort, null);
    public static SourceListOption ForFilter(string label, SourceListFilter filter) => new(label, null, filter);
}

// The sources list as the operator reads it. A projection only: the scene's own collection
// keeps the composition order, which a sort must never change.
public static class SourceListView
{
    public static IReadOnlyList<SourceItemViewModel> Apply(
        IEnumerable<SourceItemViewModel> sources, SourceListSort sort, SourceListFilter filter)
    {
        var visible = sources.Where(source => Matches(source, filter));
        // OrderBy is stable: sources that compare equal keep their scene order.
        return (sort switch
        {
            SourceListSort.NameAscending => visible.OrderBy(source => source.Name, StringComparer.CurrentCultureIgnoreCase),
            SourceListSort.NameDescending => visible.OrderByDescending(source => source.Name, StringComparer.CurrentCultureIgnoreCase),
            SourceListSort.Kind => visible.OrderBy(source => KindRank(source.Kind)),
            _ => visible
        }).ToList();
    }

    private static bool Matches(SourceItemViewModel source, SourceListFilter filter) => filter switch
    {
        SourceListFilter.Video => source.Kind == SourceKind.Video,
        SourceListFilter.Audio => source.Kind == SourceKind.Audio,
        SourceListFilter.Media => source.Kind == SourceKind.Media,
        _ => true
    };

    // What is seen before what is heard.
    private static int KindRank(SourceKind kind) => kind switch
    {
        SourceKind.Video => 0,
        SourceKind.Media => 1,
        _ => 2
    };
}
