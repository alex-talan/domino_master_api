using Application.Domino;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Domino;

public sealed class LoggingGameEventSink(ILogger<LoggingGameEventSink> logger) : IGameEventSink
{
    public void RecordTurn(TurnDecision decision)
    {
        logger.LogInformation(new EventId(1001, "TurnDecision"),
            "Turn decision: {game_id} {turn} {player} {tile} {position} {accepted} {reason}",
            decision.GameId, decision.Turn, decision.Player, decision.Tile, decision.Position, decision.Accepted, decision.Reason);
    }

    public void RecordResult(PlayerGameResult result)
    {
        logger.LogInformation(new EventId(1002, "GameResult"),
            "Game result: {game_id} {turn} {player} {win} {disqualified} {points}",
            result.GameId, result.Turn, result.Player, result.Win, result.Disqualified,
            result.YourTiles.Sum(tile => Domain.Domino.DominoTile.All[tile].Points));
    }
}