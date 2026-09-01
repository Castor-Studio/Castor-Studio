namespace CastorApplication.Services.Ai;

internal interface IAiAnalysisClient
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }
}
