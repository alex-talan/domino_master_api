using Application.Domino;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Domino;

public sealed class LoggingGameEventSink(ILogger<LoggingGameEventSink> logger) : IGameEventSink
{
    private static readonly EventId TurnDecisionEvent = new(1001, "TurnDecision");
    private static readonly EventId GameResultEvent = new(1002, "GameResult");

    public void RecordTurn(TurnDecision decision)
    {
        string playerName = PlayerName(decision.Player);
        if (decision.Accepted)
        {
            logger.LogInformation(TurnDecisionEvent,
                "{player_name} | TURN:{turn} - TABLE:[{table}] - My Tiles:[{player_tiles}] - My Move:({tile}, {position}) | game_id:{game_id} player:{player} accepted:{accepted}",
                playerName, decision.Turn, decision.Table, decision.PlayerTiles, decision.Tile, decision.Position, decision.GameId, decision.Player, decision.Accepted);
        }
        else if (decision.Tile is null)
        {
            logger.LogWarning(TurnDecisionEvent,
                "{player_name} | TURN:{turn} - TABLE:[{table}] - My Tiles:[{player_tiles}] - NO VALID RESPONSE ({reason}) - DISQUALIFIED | game_id:{game_id} player:{player} accepted:{accepted}",
                playerName, decision.Turn, decision.Table, decision.PlayerTiles, decision.Reason, decision.GameId, decision.Player, decision.Accepted);
        }
        else
        {
            logger.LogWarning(TurnDecisionEvent,
                "{player_name} | TURN:{turn} - TABLE:[{table}] - My Tiles:[{player_tiles}] - INCORRECT MOVE:({tile}, {position}) - {reason} - DISQUALIFIED | game_id:{game_id} player:{player} accepted:{accepted}",
                playerName, decision.Turn, decision.Table, decision.PlayerTiles, decision.Tile, decision.Position, decision.Reason, decision.GameId, decision.Player, decision.Accepted);
        }
    }

    public void RecordResult(PlayerGameResult result)
    {
        string outcome = result.Disqualified ? "Disqualified" : result.Win ? "WINNER" : "Did not win";
        logger.LogInformation(GameResultEvent,
            "{player_name} | {outcome} | Final Tiles:[{your_tiles}] | game_id:{game_id} turn:{turn} player:{player} win:{win} disqualified:{disqualified} points:{points}",
            PlayerName(result.Player), outcome, result.YourTiles, result.GameId, result.Turn, result.Player, result.Win, result.Disqualified,
            result.YourTiles.Sum(tile => Domain.Domino.DominoTile.All[tile].Points));
    }

    // "p0" is the protocol name; Player containers are numbered from 1.
    private static string PlayerName(string player) => $"player{int.Parse(player[1..], System.Globalization.CultureInfo.InvariantCulture) + 1}";
}