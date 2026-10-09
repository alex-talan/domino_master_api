using Domain.Domino;

namespace Application.Domino;

public sealed class DominoGameService(IPlayerClient playerClient, IDominoRandomizer randomizer)
{
    private const int PlayerCount = 4;
    private const int TilesPerPlayer = 7;
    private readonly object synchronization = new();
    private readonly List<int>[] hands = [[], [], [], []];
    private readonly List<int>[] playedTiles = [[], [], [], []];
    private readonly List<int> table = [];
    private readonly bool[] disqualified = new bool[PlayerCount];
    private int? head;
    private int? tail;
    private bool started;
    private bool ended;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (synchronization)
        {
            if (started)
            {
                throw new InvalidOperationException("The game has already started.");
            }

            started = true;
        }

        IReadOnlyList<int> shuffledTiles = randomizer.Shuffle(Enumerable.Range(0, DominoTile.All.Count).ToArray());
        for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
        {
            hands[playerIndex].AddRange(shuffledTiles.Skip(playerIndex * TilesPerPlayer).Take(TilesPerPlayer));
        }

        int consecutivePasses = 0;
        int currentPlayer = 0;
        while (!ended)
        {
            if (disqualified.All(value => value))
            {
                ended = true;
                break;
            }

            if (disqualified[currentPlayer])
            {
                currentPlayer = NextPlayer(currentPlayer);
                continue;
            }

            PlayerPlayResponse response;
            try
            {
                response = await playerClient.RequestPlayAsync(currentPlayer, BuildState(currentPlayer).ToPlayerRequest(), cancellationToken);
            }
            catch (Exception exception) when (IsPlayerFailure(exception, cancellationToken))
            {
                Disqualify(currentPlayer);
                currentPlayer = NextPlayer(currentPlayer);
                continue;
            }

            if (response.Tile == -1)
            {
                if (HasPlayableTile(currentPlayer))
                {
                    Disqualify(currentPlayer);
                    currentPlayer = NextPlayer(currentPlayer);
                    continue;
                }

                playedTiles[currentPlayer].Add(-1);
                consecutivePasses++;
                if (consecutivePasses == PlayerCount)
                {
                    await EndBlockedGameAsync(cancellationToken);
                    break;
                }

                currentPlayer = NextPlayer(currentPlayer);
                continue;
            }

            if (!TryPlay(currentPlayer, response, out _))
            {
                Disqualify(currentPlayer);
                currentPlayer = NextPlayer(currentPlayer);
                continue;
            }

            consecutivePasses = 0;
            if (hands[currentPlayer].Count == 0)
            {
                await EndNormalGameAsync(currentPlayer, cancellationToken);
                break;
            }

            currentPlayer = NextPlayer(currentPlayer);
        }
    }

    private GameState BuildState(int currentPlayer) => new(
        table.ToArray(),
        head,
        tail,
        GetPlayerTiles(0),
        GetPlayerTiles(1),
        GetPlayerTiles(2),
        GetPlayerTiles(3),
        $"p{currentPlayer}",
        hands[currentPlayer].ToArray());

    private IReadOnlyList<int>? GetPlayerTiles(int playerIndex) => disqualified[playerIndex] ? null : playedTiles[playerIndex].ToArray();

    private bool TryPlay(int playerIndex, PlayerPlayResponse response, out string? invalidReason)
    {
        invalidReason = null;
        if (response.Tile < 0 || response.Tile >= DominoTile.All.Count || !hands[playerIndex].Contains(response.Tile))
        {
            invalidReason = "The player attempted to play a tile that is not in their hand.";
            return false;
        }

        DominoTile tile = DominoTile.All[response.Tile];
        if (table.Count == 0)
        {
            table.Add(response.Tile);
            head = tile.First;
            tail = tile.Second;
        }
        else if (!Enum.TryParse(response.Position, ignoreCase: true, out DominoPosition position))
        {
            invalidReason = "A play on a non-empty table must specify head or tail.";
            return false;
        }
        else if (position == DominoPosition.Head && !tile.Contains(head!.Value))
        {
            invalidReason = "The tile does not match the table head.";
            return false;
        }
        else if (position == DominoPosition.Tail && !tile.Contains(tail!.Value))
        {
            invalidReason = "The tile does not match the table tail.";
            return false;
        }
        else if (position == DominoPosition.Head)
        {
            int currentHead = head!.Value;
            table.Insert(0, response.Tile);
            head = tile.OtherSide(currentHead);
        }
        else
        {
            int currentTail = tail!.Value;
            table.Add(response.Tile);
            tail = tile.OtherSide(currentTail);
        }

        hands[playerIndex].Remove(response.Tile);
        playedTiles[playerIndex].Add(response.Tile);
        return true;
    }

    private bool HasPlayableTile(int playerIndex) => table.Count == 0 || hands[playerIndex].Any(tileIndex => DominoTile.All[tileIndex].Contains(head!.Value) || DominoTile.All[tileIndex].Contains(tail!.Value));

    private async Task EndNormalGameAsync(int winnerIndex, CancellationToken cancellationToken)
    {
        ended = true;
        await NotifyResultsAsync(new HashSet<int> { winnerIndex }, cancellationToken);
    }

    private async Task EndBlockedGameAsync(CancellationToken cancellationToken)
    {
        ended = true;
        int lowestScore = Enumerable.Range(0, PlayerCount)
            .Where(index => !disqualified[index])
            .Select(index => hands[index].Sum(tileIndex => DominoTile.All[tileIndex].Points))
            .DefaultIfEmpty()
            .Min();
        HashSet<int> winners = Enumerable.Range(0, PlayerCount)
            .Where(index => !disqualified[index] && hands[index].Sum(tileIndex => DominoTile.All[tileIndex].Points) == lowestScore)
            .ToHashSet();
        await NotifyResultsAsync(winners, cancellationToken);
    }

    private async Task NotifyResultsAsync(IReadOnlySet<int> winners, CancellationToken cancellationToken)
    {
        for (int playerIndex = 0; playerIndex < PlayerCount; playerIndex++)
        {
            if (disqualified[playerIndex])
            {
                continue;
            }

            try
            {
                await playerClient.SendGameEndAsync(playerIndex, new GameEndNotification(winners.Contains(playerIndex), hands[playerIndex].ToArray()), cancellationToken);
            }
            catch (Exception exception) when (IsPlayerFailure(exception, cancellationToken))
            {
                _ = exception;
            }
        }
    }

    private void Disqualify(int playerIndex) => disqualified[playerIndex] = true;

    private static bool IsPlayerFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException || exception is TimeoutException ||
        exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static int NextPlayer(int playerIndex) => (playerIndex + 1) % PlayerCount;
}