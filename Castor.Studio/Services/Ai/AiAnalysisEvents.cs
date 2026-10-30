using Castor.AI.Providers;

namespace CastorApplication.Services.Ai;

/// <summary>A scene picked by the AI, with how sure it is.</summary>
internal sealed record AiSceneSwitchEvent(Guid SceneId, float Confidence);
internal sealed record AiSessionStatusEvent(AiConnectionState State, string Message);
internal sealed record AiServerErrorEvent(string ErrorCode, string ErrorMessage, bool IsFatal);
