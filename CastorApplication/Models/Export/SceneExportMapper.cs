using System.Linq;
using Castor.Engine.Models;

namespace CastorApplication.Models.Export;

public static class SceneExportMapper
{
    public static SceneExport ToExport(SceneItem scene) => new()
    {
        Name = scene.Name,
        Color = scene.Color,
        Sources = scene.Sources.Select(ToExport).ToList()
    };

    private static SourceExport ToExport(SourceItem source) => new()
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
