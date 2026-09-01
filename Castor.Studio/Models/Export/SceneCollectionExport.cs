using System.Collections.Generic;
using Castor.Engine.Models;

namespace CastorApplication.Models.Export;

public sealed class SceneCollectionExport
{
    public int Version { get; set; } = 1;
    public List<SceneExport> Scenes { get; set; } = new();
}

public sealed class SceneExport
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#5b8def";
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
}
