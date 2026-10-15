namespace Application.Domino;

public interface IGameStatisticsWriter
{
    public Task WriteAsync(IReadOnlyList<int> wins, CancellationToken cancellationToken);
}

public sealed class DominoBatchService(IPlayerClient playerClient, IDominoRandomizer randomizer, IGameStatisticsWriter statisticsWriter)
{
    private int running;

    public async Task StartAsync(int games, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(games, 1);
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            throw new InvalidOperationException("A game batch is already running.");
        }

        try
        {
            WinnerCounter counter = new();
            for (int gameIndex = 0; gameIndex < games; gameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DominoGameService game = new(playerClient, randomizer, counter);
                await game.StartAsync(cancellationToken);
            }

            await statisticsWriter.WriteAsync(counter.Wins, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    private sealed class WinnerCounter : IGameEventSink
    {
        public int[] Wins { get; } = new int[4];

        public void BeginGame()
        {
        }

        public void RecordTurn(TurnDecision decision)
        {
        }

        public void RecordResult(PlayerGameResult result)
        {
            if (result.Win)
            {
                int playerIndex = int.Parse(result.Player.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
                Wins[playerIndex]++;
            }
        }
    }
}