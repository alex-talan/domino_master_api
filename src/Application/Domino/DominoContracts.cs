namespace Application.Domino;

public sealed record PlayerPlayRequest(
    IReadOnlyList<int> Table,
    int? Head,
    int? Tail,
    IReadOnlyList<int>? Player0,
    IReadOnlyList<int>? Player1,
    IReadOnlyList<int>? Player2,
    IReadOnlyList<int>? Player3,
    string ToPlay,
    IReadOnlyList<int> YourTiles);

public sealed record PlayerPlayResponse(int Tile, string? Position);

public sealed record GameEndNotification(bool Win);

public sealed record GameState(
    IReadOnlyList<int> Table,
    int? Head,
    int? Tail,
    IReadOnlyList<int>? Player0,
    IReadOnlyList<int>? Player1,
    IReadOnlyList<int>? Player2,
    IReadOnlyList<int>? Player3,
    string ToPlay,
    IReadOnlyList<int> YourTiles)
{
    public PlayerPlayRequest ToPlayerRequest() => new(Table, Head, Tail, Player0, Player1, Player2, Player3, ToPlay, YourTiles);
}

public interface IPlayerClient
{
    public Task<PlayerPlayResponse> RequestPlayAsync(int playerIndex, PlayerPlayRequest request, CancellationToken cancellationToken);

    public Task SendGameEndAsync(int playerIndex, GameEndNotification notification, CancellationToken cancellationToken);
}

public interface IDominoRandomizer
{
    public IReadOnlyList<int> Shuffle(IReadOnlyList<int> values);
}