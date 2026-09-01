namespace CastorApplication.Models.Studio;

public sealed class SceneDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#5b8def";
    public List<SourceDefinition> Sources { get; set; } = [];
}
