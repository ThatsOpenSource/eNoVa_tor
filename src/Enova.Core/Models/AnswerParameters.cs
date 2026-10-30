namespace Lragrz.Enova.Core.Models;

/// <summary>
/// Parameters supplied by the user interface when generating an answer message.
/// </summary>
public sealed class AnswerParameters
{
    public required string FileNumber { get; init; }
    public required string MessageId { get; init; }
    public required string CaseworkerName { get; init; }
    public IReadOnlyList<string> DecisionCodes { get; init; } = Array.Empty<string>();
    public string? ProductName { get; init; }
    public string? ManufacturerName { get; init; }
    public string? Version { get; init; }
}

