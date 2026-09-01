using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Castor.Engine.Models;
using Castor.Engine.Services;
using Castor.IA.Proto;
using CastorApplication.Services.Config;

namespace CastorApplication.Services.Ai;

public sealed class GrpcAiAnalysisClient : IAiAnalysisClient
{
    private readonly IConfigService _configService;
    private readonly IStudioController _studioController;
    private readonly SemaphoreSlim _sync = new(1, 1);

    private GrpcChannel? _channel;
    private IaAnalysisService.IaAnalysisServiceClient? _client;
    private AsyncDuplexStreamingCall<ClientMessage, ServerEvent>? _stream;
    private CancellationTokenSource? _streamCancellation;
    private Task? _readTask;
    private Task? _keepAliveTask;
    private bool _isStoppingSession;

    public bool HasActiveSession => !string.IsNullOrWhiteSpace(SessionId);
    public string? SessionId { get; private set; }

    public event Action<AiSceneSwitchEvent>? SceneSwitchSuggested;
    public event Action<AiSessionStatusEvent>? SessionStatusChanged;
    public event Action<AiServerErrorEvent>? ServerErrorReceived;

    public GrpcAiAnalysisClient(IConfigService configService, IStudioController studioController)
    {
        _configService = configService;
        _studioController = studioController;
    }

    public async Task StartSessionAsync(
        string moduleName,
        IReadOnlyDictionary<string, string>? moduleConfig,
        CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (HasActiveSession) return;

            EnsureClient();

            var request = new StartSessionRequest
            {
                ModuleName = moduleName
            };

            if (moduleConfig != null)
            {
                foreach (var (key, value) in moduleConfig)
                {
                    request.ModuleConfig[key] = value;
                }
            }

            var response = await _client!.StartSessionAsync(request, cancellationToken: cancellationToken);
            if (!response.Success || string.IsNullOrWhiteSpace(response.SessionId))
            {
                var message = string.IsNullOrWhiteSpace(response.Message)
                    ? "AI server rejected the session."
                    : response.Message;
                throw new InvalidOperationException($"{message} {response.ErrorCode}".Trim());
            }

            SessionId = response.SessionId;
            _streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _stream = _client.AnalysisStream(cancellationToken: _streamCancellation.Token);
            _readTask = Task.Run(() => ReadServerEventsAsync(_streamCancellation.Token));
            _keepAliveTask = Task.Run(() => KeepAliveAsync(_streamCancellation.Token));
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SendSourcesAsync(IReadOnlyList<SceneItem> scenes, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!HasActiveSession || _stream == null)
                throw new InvalidOperationException("No active AI session.");

            var sourceList = new SourceList();
            foreach (var scene in scenes)
            {
                if (!_studioController.HasVideoSource(scene)) continue;

                var previewResult = _studioController.EnsurePreview(scene);
                if (previewResult != 0 && previewResult != -1)
                    throw new InvalidOperationException($"Preview failed for scene '{scene.Name}' (code {previewResult}).");

                sourceList.Sources.Add(new Source
                {
                    SceneId = scene.Id.ToString("N"),
                    Label = scene.Name,
                    Url = _studioController.GetPreviewPullUrl(scene.Id)
                });
            }

            if (sourceList.Sources.Count == 0)
                throw new InvalidOperationException("No selected scene has a video source.");

            await _stream.RequestStream.WriteAsync(new ClientMessage
            {
                SessionId = SessionId,
                Sources = sourceList
            }, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task StopSessionAsync(string reason, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!HasActiveSession) return;

            _isStoppingSession = true;
            var sessionId = SessionId!;

            if (_stream != null)
            {
                try
                {
                    await _stream.RequestStream.WriteAsync(new ClientMessage
                    {
                        SessionId = sessionId,
                        Stop = new StopSignal { Reason = reason }
                    }, cancellationToken);
                    await _stream.RequestStream.CompleteAsync();
                }
                catch
                {
                }
            }

            if (_client != null)
            {
                try
                {
                    await _client.EndSessionAsync(new EndSessionRequest { SessionId = sessionId }, cancellationToken: cancellationToken);
                }
                catch
                {
                }
            }

            await ClearSessionAsync();
        }
        finally
        {
            _isStoppingSession = false;
            _sync.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopSessionAsync("desktop_dispose", CancellationToken.None);
        _channel?.Dispose();
        _sync.Dispose();
    }

    private void EnsureClient()
    {
        if (_client != null) return;

        _channel = GrpcChannel.ForAddress(_configService.Config.AiServer.Endpoint);
        _client = new IaAnalysisService.IaAnalysisServiceClient(_channel);
    }

    private async Task ReadServerEventsAsync(CancellationToken cancellationToken)
    {
        if (_stream == null) return;

        try
        {
            await foreach (var serverEvent in _stream.ResponseStream.ReadAllAsync(cancellationToken))
            {
                switch (serverEvent.PayloadCase)
                {
                    case ServerEvent.PayloadOneofCase.SwitchSuggestion:
                        SceneSwitchSuggested?.Invoke(new AiSceneSwitchEvent(
                            serverEvent.SwitchSuggestion.SceneId,
                            serverEvent.SwitchSuggestion.Confidence));
                        break;
                    case ServerEvent.PayloadOneofCase.Status:
                        SessionStatusChanged?.Invoke(new AiSessionStatusEvent(
                            serverEvent.Status.State,
                            serverEvent.Status.Message));
                        break;
                    case ServerEvent.PayloadOneofCase.Error:
                        if (_isStoppingSession && IsSessionNotFound(serverEvent.Error.ErrorCode))
                            break;

                        ServerErrorReceived?.Invoke(new AiServerErrorEvent(
                            serverEvent.Error.ErrorCode,
                            serverEvent.Error.ErrorMessage,
                            serverEvent.Error.IsFatal));
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
        }
        catch (Exception ex)
        {
            if (_isStoppingSession) return;

            ServerErrorReceived?.Invoke(new AiServerErrorEvent("CLIENT_STREAM_ERROR", ex.Message, true));
        }
    }

    private async Task KeepAliveAsync(CancellationToken cancellationToken)
    {
        if (_stream == null) return;

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!HasActiveSession) return;

                await _stream.RequestStream.WriteAsync(new ClientMessage
                {
                    SessionId = SessionId,
                    KeepAlive = new KeepAlive
                    {
                        TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_isStoppingSession) return;

            ServerErrorReceived?.Invoke(new AiServerErrorEvent("CLIENT_KEEPALIVE_ERROR", ex.Message, true));
        }
    }

    private async Task ClearSessionAsync()
    {
        SessionId = null;
        _streamCancellation?.Cancel();

        var tasks = new[] { _readTask, _keepAliveTask }.Where(task => task != null).Cast<Task>().ToArray();
        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
            }
        }

        _stream?.Dispose();
        _streamCancellation?.Dispose();
        _stream = null;
        _streamCancellation = null;
        _readTask = null;
        _keepAliveTask = null;
    }

    private static bool IsSessionNotFound(string? errorCode) =>
        string.Equals(errorCode, "SESSION_NOT_FOUND", StringComparison.OrdinalIgnoreCase);
}
