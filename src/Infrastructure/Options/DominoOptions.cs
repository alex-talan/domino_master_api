namespace Infrastructure.Options;

public sealed class DominoOptions
{
    public const string SectionName = "Domino";

    public string[] PlayerEndpoints { get; init; } = [];

    public int PlayerTimeoutSeconds { get; init; } = 10;
}