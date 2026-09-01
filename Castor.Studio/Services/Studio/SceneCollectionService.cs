using System.Text.Json;
using CastorApplication.Models.Export;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Settings;

namespace CastorApplication.Services.Studio;

internal sealed class SceneCollectionService : ISceneCollectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Task SaveAsync(string path, IReadOnlyCollection<SceneDefinition> scenes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var export = new SceneCollectionExport
        {
            Scenes = scenes.Select(SceneExportMapper.ToExport).ToList()
        };
        SettingsService.WriteFileAtomically(path, JsonSerializer.Serialize(export, JsonOptions));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SceneDefinition>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var export = JsonSerializer.Deserialize<SceneCollectionExport>(json, JsonOptions);
        if (export == null)
            return [];

        if (export.Version != 1)
            throw new InvalidDataException($"Version d'export non prise en charge : {export.Version}.");

        if (export.Scenes.Count == 0)
            return [];

        return export.Scenes.Select(SceneExportMapper.FromExport).ToArray();
    }
}
