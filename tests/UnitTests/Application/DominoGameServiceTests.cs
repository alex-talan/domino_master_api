using Application.Domino;
using FluentAssertions;
using NSubstitute;

namespace UnitTests.Application;

public sealed class DominoGameServiceTests
{
    [Fact]
    public async Task StartAsync_ShouldDisqualifyPlayerThatTimesOut()
    {
        RecordingPlayerClient playerClient = new();
        RecordingGameEventSink eventSink = new();
        DominoGameService sut = new(playerClient, new OrderedRandomizer(), eventSink);

        await sut.StartAsync(CancellationToken.None);

        playerClient.SawDisqualifiedPlayerState.Should().BeTrue();
        playerClient.SawCurrentTurnAndHand.Should().BeTrue();
        playerClient.GameEndNotifications.Select(notification => notification.PlayerIndex).Should().NotContain(1);
        playerClient.GameEndNotifications.Should().HaveCount(3);
        playerClient.GameEndNotifications.Single(notification => notification.PlayerIndex == 0).Notification.Win.Should().BeTrue();
        playerClient.GameEndNotifications.Single(notification => notification.PlayerIndex == 0).Notification.YourTiles.Should().BeEmpty();
        playerClient.GameEndNotifications.Where(notification => notification.PlayerIndex != 0).Should().AllSatisfy(notification => notification.Notification.Win.Should().BeFalse());
        playerClient.GameEndNotifications.Should().AllSatisfy(notification =>
            notification.Notification.YourTiles.Should().BeEquivalentTo(playerClient.ExpectedRemainingTiles[notification.PlayerIndex]));
        Guid gameId = playerClient.Requests[0].GameId;
        gameId.Should().NotBeEmpty();
        playerClient.Requests.Select(request => request.Turn).Should().Equal(Enumerable.Range(1, playerClient.Requests.Count));
        playerClient.Requests.Should().AllSatisfy(request => request.GameId.Should().Be(gameId));
        eventSink.Decisions.Select(decision => decision.Turn).Should().Equal(playerClient.Requests.Select(request => request.Turn));
        eventSink.Decisions.Should().AllSatisfy(decision =>
        {
            PlayerPlayRequest request = playerClient.Requests[decision.Turn - 1];
            decision.GameId.Should().Be(request.GameId);
            decision.Player.Should().Be(request.ToPlay);
        });
        eventSink.Decisions.Single(decision => decision.Turn == 2).Should().Be(
            new TurnDecision(gameId, 2, "p1", null, string.Empty, false, "player_failure"));
        eventSink.Decisions.Should().Contain(decision => decision.Tile == -1 && decision.Accepted);
        eventSink.Results.Should().HaveCount(4);
        eventSink.Results.Single(result => result.Player == "p1").Disqualified.Should().BeTrue();
        eventSink.Results.Should().AllSatisfy(result =>
        {
            result.GameId.Should().Be(gameId);
            result.Turn.Should().Be(playerClient.Requests.Count);
        });
        playerClient.GameEndNotifications.Should().AllSatisfy(notification =>
        {
            notification.Notification.GameId.Should().Be(gameId);
            notification.Notification.Turn.Should().Be(playerClient.Requests.Count);
        });
        eventSink.TurnCountAtFirstResult.Should().Be(playerClient.Requests.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(27)]
    public async Task StartAsync_ShouldRecordRejectedMovesAndFinalResults(int proposedTile)
    {
        IPlayerClient playerClient = Substitute.For<IPlayerClient>();
        playerClient.RequestPlayAsync(Arg.Any<int>(), Arg.Any<PlayerPlayRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerPlayResponse(proposedTile, "tail")));
        RecordingGameEventSink eventSink = new();
        DominoGameService sut = new(playerClient, new OrderedRandomizer(), eventSink);

        await sut.StartAsync(CancellationToken.None);

        eventSink.Decisions.Should().Contain(decision => decision.Tile == proposedTile && !decision.Accepted && decision.Reason != null);
        eventSink.Decisions.Select(decision => decision.Turn).Should().Equal(Enumerable.Range(1, eventSink.Decisions.Count));
        eventSink.Results.Should().HaveCount(4);
        eventSink.Results.Should().OnlyContain(result => result.Disqualified && !result.Win);
        await playerClient.DidNotReceive().SendGameEndAsync(Arg.Any<int>(), Arg.Any<GameEndNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ShouldAssignDifferentIdsToSeparateGames()
    {
        IPlayerClient playerClient = Substitute.For<IPlayerClient>();
        playerClient.RequestPlayAsync(Arg.Any<int>(), Arg.Any<PlayerPlayRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerPlayResponse(-1, string.Empty)));
        RecordingGameEventSink firstEvents = new();
        RecordingGameEventSink secondEvents = new();

        await new DominoGameService(playerClient, new OrderedRandomizer(), firstEvents).StartAsync(CancellationToken.None);
        await new DominoGameService(playerClient, new OrderedRandomizer(), secondEvents).StartAsync(CancellationToken.None);

        firstEvents.Decisions[0].GameId.Should().NotBe(secondEvents.Decisions[0].GameId);
        firstEvents.Decisions[0].Turn.Should().Be(1);
        secondEvents.Decisions[0].Turn.Should().Be(1);
    }

    private sealed class RecordingGameEventSink : IGameEventSink
    {
        public List<TurnDecision> Decisions { get; } = [];

        public List<PlayerGameResult> Results { get; } = [];

        public int? TurnCountAtFirstResult { get; private set; }

        public void RecordTurn(TurnDecision decision) => Decisions.Add(decision);

        public void RecordResult(PlayerGameResult result)
        {
            TurnCountAtFirstResult ??= Decisions.Count;
            Results.Add(result);
        }
    }

    private sealed class OrderedRandomizer : IDominoRandomizer
    {
        public IReadOnlyList<int> Shuffle(IReadOnlyList<int> values) => values;
    }

    private sealed class RecordingPlayerClient : IPlayerClient
    {
        public List<PlayerPlayRequest> Requests { get; } = [];

        public Dictionary<int, IReadOnlyList<int>> ExpectedRemainingTiles { get; } = [];

        public List<(int PlayerIndex, GameEndNotification Notification)> GameEndNotifications { get; } = [];

        public bool SawDisqualifiedPlayerState { get; private set; }

        public bool SawCurrentTurnAndHand { get; private set; }

        public Task<PlayerPlayResponse> RequestPlayAsync(int playerIndex, PlayerPlayRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (playerIndex == 1)
            {
                throw new TimeoutException();
            }

            SawDisqualifiedPlayerState |= request.Player1 is null;
            SawCurrentTurnAndHand |= request.ToPlay == $"p{playerIndex}" && request.YourTiles.Count > 0;
            HashSet<int> tiles = request.YourTiles.ToHashSet();
            ExpectedRemainingTiles[playerIndex] = tiles.ToArray();
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
            ExpectedRemainingTiles[playerIndex] = tiles.ToArray();
            return Task.FromResult(new PlayerPlayResponse(selectedTile.Value, position));
        }

        public Task SendGameEndAsync(int playerIndex, GameEndNotification notification, CancellationToken cancellationToken)
        {
            GameEndNotifications.Add((playerIndex, notification));
            return Task.CompletedTask;
        }
    }
}