using Application.Domino;

namespace Infrastructure.Domino;

public sealed class GameStatisticsWriter(string outputPath = "output.log") : IGameStatisticsWriter
{
    public Task WriteAsync(IReadOnlyList<int> wins, CancellationToken cancellationToken)
    {
        string[] lines = wins.Select((count, index) => $"Player{index + 1} wins {count} games").ToArray();
        return File.WriteAllLinesAsync(outputPath, lines, cancellationToken);
    }
}