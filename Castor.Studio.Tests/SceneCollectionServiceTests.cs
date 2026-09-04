using System.Text.Json;
using CastorApplication.Models.Export;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

public sealed class SceneCollectionServiceTests
{
    [Fact]
    public async Task Version_one_round_trips_without_losing_source_metadata()
    {
        var service = new SceneCollectionService();
        var path = Path.Combine(Path.GetTempPath(), $"castor-scenes-{Guid.NewGuid():N}.json");
        try
        {
            var scenes = new[]
            {
                new SceneDefinition
                {
                    Name = "Live",
                    Color = "#123456",
                    Sources =
                    [
                        new SourceDefinition
                        {
                            Name = "Caméra réseau", Kind = SourceKind.Video, Origin = SourceOrigin.Network,
                            OriginLabel = "Caméra", OriginPath = "rtsp://example/stream", Loop = false
                        },
                        new SourceDefinition
                        {
                            Name = "Générique", Kind = SourceKind.Media, Origin = SourceOrigin.File,
                            OriginLabel = "generic.wav", OriginPath = @"C:\media\generic.wav", Loop = true
                        }
                    ]
                }
            };

            await service.SaveAsync(path, scenes, CancellationToken.None);
            var loaded = await service.LoadAsync(path, CancellationToken.None);

            var scene = Assert.Single(loaded);
            Assert.Equal(2, scene.Sources.Count);
            var source = scene.Sources.Single(item => item.Origin == SourceOrigin.Network);
            var media = scene.Sources.Single(item => item.Origin == SourceOrigin.File);
            Assert.Equal("Live", scene.Name);
            Assert.Equal("rtsp://example/stream", source.OriginPath);
            Assert.Equal(SourceOrigin.Network, source.Origin);
            Assert.Equal(SourceKind.Media, media.Kind);
            Assert.True(media.Loop);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Unsupported_export_version_is_rejected()
    {
        var service = new SceneCollectionService();
        var path = Path.Combine(Path.GetTempPath(), $"castor-scenes-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new SceneCollectionExport { Version = 2 }));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(path, CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
