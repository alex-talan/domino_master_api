using Application.Domino;

namespace Infrastructure.Domino;

public sealed class GameStatisticsWriter(string outputPath = "output.log") : IGameStatisticsWriter
{
    public Task WriteAsync(IReadOnlyList<int> teamWins, CancellationToken cancellationToken)
    {
        string[] lines = teamWins.Select((count, team) => $"Team{team} (Player{team + 1} and Player{team + 3}) wins {count} games").ToArray();
        return File.WriteAllLinesAsync(outputPath, lines, cancellationToken);
    }
}