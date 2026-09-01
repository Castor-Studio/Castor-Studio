using System.Linq;
using CastorApplication.Models.Studio;

namespace CastorApplication.Models.Export;

public static class SceneExportMapper
{
    public static SceneExport ToExport(SceneDefinition scene) => new()
    {
        Name = scene.Name,
        Color = scene.Color,
        Sources = scene.Sources.Select(ToExport).ToList()
    };

    public static SceneDefinition FromExport(SceneExport scene) => new()
    {
        Name = scene.Name,
        Color = scene.Color,
        Sources = scene.Sources.Select(FromExport).ToList()
    };

    private static SourceExport ToExport(SourceDefinition source) => new()
    {
        Name = source.Name,
        Kind = source.Kind,
        Color = source.Color,
        Loop = source.Loop,
        Origin = source.Origin,
        OriginLabel = source.OriginLabel,
        OriginPath = source.OriginPath
    };

    private static SourceDefinition FromExport(SourceExport source) => new()
    {
        Name = source.Name,
        Kind = source.Kind,
        Color = source.Color,
        Loop = source.Loop,
        Origin = source.Origin,
        OriginLabel = source.OriginLabel,
        OriginPath = source.OriginPath
    };
}
