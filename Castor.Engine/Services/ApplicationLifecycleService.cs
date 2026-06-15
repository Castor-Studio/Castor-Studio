namespace Castor.Engine.Services;

public sealed class ApplicationLifecycleService(
    INativeCaptureService nativeCaptureService,
    IMediaMtxService mediaMtxService,
    IRecorderService recorderService) : IApplicationLifecycleService
{
    public void Start()
    {
        nativeCaptureService.Initialize();
        mediaMtxService.Start();
    }

    public void Stop()
    {
        recorderService.StopAll();
        mediaMtxService.Stop();
    }
}
