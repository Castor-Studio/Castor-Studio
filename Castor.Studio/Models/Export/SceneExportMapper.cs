using CastorApplication.Models.Studio;

namespace CastorApplication.Models.Export;

public static class SceneExportMapper
{
    public static SceneExport ToExport(SceneDefinition scene) => new()
    {
        Name = scene.Name,
        Color = scene.Color,
        CanvasWidth = scene.CanvasWidth,
        CanvasHeight = scene.CanvasHeight,
        Sources = scene.Sources.Select(ToExport).ToList()
    };

    public static SceneDefinition FromExport(SceneExport scene) => new()
    {
        Name = scene.Name,
        Color = scene.Color,
        CanvasWidth = scene.CanvasWidth,
        CanvasHeight = scene.CanvasHeight,
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
        OriginPath = source.OriginPath,
        VideoCaptureKind = source.VideoCaptureKind,
        AudioCaptureKind = source.AudioCaptureKind,
        Layout = source.Layout is { } layout ? ToExport(layout) : null
    };

    private static SourceDefinition FromExport(SourceExport source) => new()
    {
        Name = source.Name,
        Kind = source.Kind,
        Color = source.Color,
        Loop = source.Loop,
        Origin = source.Origin,
        OriginLabel = source.OriginLabel,
        OriginPath = source.OriginPath,
        VideoCaptureKind = source.VideoCaptureKind,
        AudioCaptureKind = source.AudioCaptureKind,
        Layout = source.Layout is { } layout ? FromExport(layout) : null
    };

    private static SourceLayoutExport ToExport(SourceLayout layout) => new()
    {
        X = layout.Placement.X,
        Y = layout.Placement.Y,
        ScaleX = layout.Placement.ScaleX,
        ScaleY = layout.Placement.ScaleY,
        Rotation = layout.Placement.Rotation,
        CropLeft = layout.Placement.Crop.Left,
        CropTop = layout.Placement.Crop.Top,
        CropRight = layout.Placement.Crop.Right,
        CropBottom = layout.Placement.Crop.Bottom,
        Zoom = layout.Zoom.Factor,
        ZoomCenterX = layout.Zoom.CenterX,
        ZoomCenterY = layout.Zoom.CenterY
    };

    private static SourceLayout FromExport(SourceLayoutExport layout) => new(
        new SourcePlacement(
            layout.X,
            layout.Y,
            layout.ScaleX,
            layout.ScaleY,
            new SourceCrop(layout.CropLeft, layout.CropTop, layout.CropRight, layout.CropBottom),
            layout.Rotation),
        new SourceZoom(layout.Zoom, layout.ZoomCenterX, layout.ZoomCenterY));
}
