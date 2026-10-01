using Application.Domino;
using FluentAssertions;

namespace UnitTests.Application;

public sealed class DominoGameServiceTests
{
    [Fact]
    public async Task StartAsync_ShouldDisqualifyPlayerThatTimesOut()
    {
        RecordingPlayerClient playerClient = new();
        DominoGameService sut = new(playerClient, new OrderedRandomizer());

        await sut.StartAsync(CancellationToken.None);

        playerClient.TilesSent.Should().HaveCount(4);
        playerClient.SawDisqualifiedPlayerState.Should().BeTrue();
        playerClient.GameEndNotifications.Select(notification => notification.PlayerIndex).Should().NotContain(1);
        playerClient.GameEndNotifications.Should().HaveCount(3);
        playerClient.GameEndNotifications.Single(notification => notification.PlayerIndex == 0).Notification.Win.Should().BeTrue();
        playerClient.GameEndNotifications.Where(notification => notification.PlayerIndex != 0).Should().AllSatisfy(notification => notification.Notification.Win.Should().BeFalse());
    }

    private sealed class OrderedRandomizer : IDominoRandomizer
    {
        public IReadOnlyList<int> Shuffle(IReadOnlyList<int> values) => values;
    }

    private sealed class RecordingPlayerClient : IPlayerClient
    {
        private readonly Dictionary<int, HashSet<int>> remainingTiles = [];

        public List<(int PlayerIndex, IReadOnlyList<int> Tiles)> TilesSent { get; } = [];

        public List<(int PlayerIndex, GameEndNotification Notification)> GameEndNotifications { get; } = [];

        public bool SawDisqualifiedPlayerState { get; private set; }

        public Task SendTilesAsync(int playerIndex, IReadOnlyList<int> tiles, CancellationToken cancellationToken)
        {
            remainingTiles[playerIndex] = tiles.ToHashSet();
            TilesSent.Add((playerIndex, tiles));
            return Task.CompletedTask;
        }

        public Task<PlayerPlayResponse> RequestPlayAsync(int playerIndex, PlayerPlayRequest request, CancellationToken cancellationToken)
        {
            if (playerIndex == 1)
            {
                throw new TimeoutException();
            }

            SawDisqualifiedPlayerState |= request.Player1 is null;
            HashSet<int> tiles = remainingTiles[playerIndex];
            int? selectedTile = request.Head is null
                ? tiles.FirstOrDefault()
                : tiles.FirstOrDefault(tile => Domain.Domino.DominoTile.All[tile].Contains(request.Head.Value) || Domain.Domino.DominoTile.All[tile].Contains(request.Tail!.Value));

            if (selectedTile is null || !tiles.Contains(selectedTile.Value))
            {
                return Task.FromResult(new PlayerPlayResponse(-1, null));
            }

            Domain.Domino.DominoTile tile = Domain.Domino.DominoTile.All[selectedTile.Value];
            string position = request.Head is not null && tile.Contains(request.Head.Value) ? "head" : "tail";
            tiles.Remove(selectedTile.Value);
            return Task.FromResult(new PlayerPlayResponse(selectedTile.Value, position));
        }

        public Task SendGameEndAsync(int playerIndex, GameEndNotification notification, CancellationToken cancellationToken)
        {
            GameEndNotifications.Add((playerIndex, notification));
            return Task.CompletedTask;
        }
    }
}