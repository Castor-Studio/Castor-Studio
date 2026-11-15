using CastorApplication.Models.Studio;

namespace CastorApplication.Models.Export;

// Version 1 only grows by optional fields: a file written before them still reads, and a
// field missing from it keeps the engine's default.
public sealed class SceneCollectionExport
{
    public int Version { get; set; } = 1;
    public List<SceneExport> Scenes { get; set; } = new();
}

public sealed class SceneExport
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#5b8def";

    // Canvas the layouts below were composed on; 0 when unknown.
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }

    // From foreground to background.
    public List<SourceExport> Sources { get; set; } = new();
}

public sealed class SourceExport
{
    public string Name { get; set; } = "";
    public SourceKind Kind { get; set; }
    public string Color { get; set; } = "#5b8def";
    public bool Loop { get; set; }
    public SourceOrigin Origin { get; set; }
    public string OriginLabel { get; set; } = "";
    public string OriginPath { get; set; } = "";

    // What a hardware source captures: with OriginPath, the device id, it is enough to open the
    // device again. Null for file sources and in files written before.
    public VideoCaptureKind? VideoCaptureKind { get; set; }
    public AudioCaptureKind? AudioCaptureKind { get; set; }

    public SourceLayoutExport? Layout { get; set; }
}

public sealed class SourceLayoutExport
{
    public double X { get; set; }
    public double Y { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double Rotation { get; set; }
    public int CropLeft { get; set; }
    public int CropTop { get; set; }
    public int CropRight { get; set; }
    public int CropBottom { get; set; }
    public double Zoom { get; set; } = SourceZoom.MinFactor;
    public double ZoomCenterX { get; set; } = 0.5;
    public double ZoomCenterY { get; set; } = 0.5;
}
