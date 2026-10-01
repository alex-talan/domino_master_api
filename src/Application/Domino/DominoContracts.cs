namespace Application.Domino;

public sealed record PlayerPlayRequest(IReadOnlyList<int> Table, int? Head, int? Tail, IReadOnlyList<int>? Player0, IReadOnlyList<int>? Player1, IReadOnlyList<int>? Player2, IReadOnlyList<int>? Player3);

public sealed record PlayerPlayResponse(int Tile, string? Position);

public sealed record GameEndNotification(bool Win);

public sealed record GameState(
    IReadOnlyList<int> Table,
    int? Head,
    int? Tail,
    IReadOnlyList<int>? Player0,
    IReadOnlyList<int>? Player1,
    IReadOnlyList<int>? Player2,
    IReadOnlyList<int>? Player3)
{
    public PlayerPlayRequest ToPlayerRequest() => new(Table, Head, Tail, Player0, Player1, Player2, Player3);
}

public interface IPlayerClient
{
    public Task SendTilesAsync(int playerIndex, IReadOnlyList<int> tiles, CancellationToken cancellationToken);

    public Task<PlayerPlayResponse> RequestPlayAsync(int playerIndex, PlayerPlayRequest request, CancellationToken cancellationToken);

    public Task SendGameEndAsync(int playerIndex, GameEndNotification notification, CancellationToken cancellationToken);
}

public interface IDominoRandomizer
{
    public IReadOnlyList<int> Shuffle(IReadOnlyList<int> values);
}