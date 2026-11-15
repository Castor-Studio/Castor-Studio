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
    public async Task Source_layouts_and_canvas_round_trip()
    {
        var service = new SceneCollectionService();
        var path = Path.Combine(Path.GetTempPath(), $"castor-scenes-{Guid.NewGuid():N}.json");
        var layout = new SourceLayout(
            new SourcePlacement(12.5, -40, 0.75, 1.25, new SourceCrop(1, 2, 3, 4), 45),
            new SourceZoom(2.5, 0.25, 0.75));
        try
        {
            await service.SaveAsync(path,
            [
                new SceneDefinition
                {
                    Name = "Live",
                    CanvasWidth = 2560,
                    CanvasHeight = 1440,
                    Sources =
                    [
                        new SourceDefinition
                        {
                            Name = "Caméra", Origin = SourceOrigin.HardwareVideo, OriginPath = "cam-id",
                            VideoCaptureKind = VideoCaptureKind.Camera, Layout = layout
                        },
                        new SourceDefinition
                        {
                            Name = "Fond", Origin = SourceOrigin.HardwareAudio, OriginPath = "mic-id",
                            AudioCaptureKind = AudioCaptureKind.Microphone
                        }
                    ]
                }
            ], CancellationToken.None);

            var scene = Assert.Single(await service.LoadAsync(path, CancellationToken.None));

            Assert.Equal((2560, 1440), (scene.CanvasWidth, scene.CanvasHeight));
            Assert.Equal(["Caméra", "Fond"], scene.Sources.Select(source => source.Name));
            Assert.Equal(layout, scene.Sources[0].Layout);
            Assert.Null(scene.Sources[1].Layout);
            Assert.Equal(VideoCaptureKind.Camera, scene.Sources[0].VideoCaptureKind);
            Assert.Equal(AudioCaptureKind.Microphone, scene.Sources[1].AudioCaptureKind);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task A_file_written_before_layouts_still_loads()
    {
        var service = new SceneCollectionService();
        var path = Path.Combine(Path.GetTempPath(), $"castor-scenes-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path,
                """{"Version":1,"Scenes":[{"Name":"Ancienne","Color":"#5b8def","Sources":[{"Name":"Clip","Kind":2,"Origin":3,"OriginPath":"C:\\clip.mp4"}]}]}""");

            var scene = Assert.Single(await service.LoadAsync(path, CancellationToken.None));

            Assert.Equal(0, scene.CanvasWidth);
            Assert.Null(Assert.Single(scene.Sources).Layout);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void A_layout_follows_a_canvas_change_except_its_crop()
    {
        var layout = new SourceLayout(new SourcePlacement(100, 50, 1, 1, new SourceCrop(8, 8, 8, 8), 30), new SourceZoom(2));

        Assert.Equal(
            new SourcePlacement(200, 100, 2, 2, new SourceCrop(8, 8, 8, 8), 30),
            layout.Rescaled(1920, 1080, 3840, 2160).Placement);
        Assert.Same(layout, layout.Rescaled(0, 0, 3840, 2160));
        Assert.Same(layout, layout.Rescaled(1920, 1080, 1920, 1080));
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

    [Fact]
    public async Task Saving_creates_the_missing_folder()
    {
        var service = new SceneCollectionService();
        var directory = Path.Combine(Path.GetTempPath(), $"castor-scenes-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "scenes.json");
        try
        {
            await service.SaveAsync(path, [new SceneDefinition { Name = "Plateau" }], CancellationToken.None);

            var loaded = await service.LoadAsync(path, CancellationToken.None);
            Assert.Equal("Plateau", Assert.Single(loaded).Name);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
