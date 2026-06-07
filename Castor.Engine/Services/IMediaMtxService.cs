namespace Castor.Engine.Services;

public interface IMediaMtxService
{
    bool IsRunning { get; }
    string GetPreviewPushUrl(Guid sceneId);
    string GetPreviewPullUrl(Guid sceneId);
    void Start();
    void Stop();
}
