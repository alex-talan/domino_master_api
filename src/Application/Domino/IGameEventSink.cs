namespace Application.Domino;

public sealed record TurnDecision(Guid GameId, int Turn, string Player, int? Tile, string Position, bool Accepted, string? Reason);

public sealed record PlayerGameResult(Guid GameId, int Turn, string Player, bool Win, bool Disqualified, IReadOnlyList<int> YourTiles);

public interface IGameEventSink
{
    public void RecordTurn(TurnDecision decision);

    public void RecordResult(PlayerGameResult result);
}