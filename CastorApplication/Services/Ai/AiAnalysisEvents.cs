using Castor.IA.Proto;

namespace CastorApplication.Services.Ai;

public sealed record AiSceneSwitchEvent(string SceneId, float Confidence);

public sealed record AiSessionStatusEvent(SessionState State, string Message);

public sealed record AiServerErrorEvent(string ErrorCode, string ErrorMessage, bool IsFatal);
