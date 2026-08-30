namespace SurvivalBackend.Services;

public sealed record WipeOperationState(
    string Status,
    string? TriggeredBy,
    string? CurrentStep,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? LastError)
{
    public static WipeOperationState Idle { get; } = new(
        Status: "Idle",
        TriggeredBy: null,
        CurrentStep: null,
        StartedAtUtc: null,
        FinishedAtUtc: null,
        LastError: null);
}
