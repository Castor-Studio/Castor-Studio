namespace CastorApplication.Services.Ai;

internal sealed class UnavailableAiAnalysisClient : IAiAnalysisClient
{
    public bool IsAvailable => false;
    public string UnavailableMessage => "L'analyse IA nécessite le runtime LibObs.";
}
