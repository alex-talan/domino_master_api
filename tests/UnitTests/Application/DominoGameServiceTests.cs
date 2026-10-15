using Application.Domino;
using FluentAssertions;
using NSubstitute;

namespace UnitTests.Application;

public sealed class DominoGameServiceTests
{
    [Fact]
    public async Task Batch_ShouldStartFreshGamesAndWriteOnlyWinTotals()
    {
        RecordingPlayerClient playerClient = new();
        IGameStatisticsWriter writer = Substitute.For<IGameStatisticsWriter>();
        DominoBatchService sut = new(playerClient, new OrderedRandomizer(), writer);

        await sut.StartAsync(3, CancellationToken.None);

        PlayerPlayRequest[] firstTurns = playerClient.Requests.Where(request => request.Turn == 1).ToArray();
        firstTurns.Should().HaveCount(3);
        firstTurns.Select(request => request.GameId).Distinct().Should().HaveCount(3);
        firstTurns.Should().AllSatisfy(request =>
        {
            request.ToPlay.Should().Be("p0");
            request.Table.Should().BeEmpty();
            request.YourTiles.Should().HaveCount(7);
            request.Player0.Should().BeEmpty();
            request.Player1.Should().BeEmpty();
        });
        await writer.Received(1).WriteAsync(Arg.Is<IReadOnlyList<int>>(wins => wins.SequenceEqual(new[] { 3, 0, 0, 0 })), CancellationToken.None);

        await sut.StartAsync(1, CancellationToken.None);

        await writer.Received(1).WriteAsync(Arg.Is<IReadOnlyList<int>>(wins => wins.SequenceEqual(new[] { 1, 0, 0, 0 })), CancellationToken.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Batch_ShouldRejectInvalidGameCount(int games)
    {
        IGameStatisticsWriter writer = Substitute.For<IGameStatisticsWriter>();
        DominoBatchService sut = new(Substitute.For<IPlayerClient>(), new OrderedRandomizer(), writer);

        Func<Task> action = () => sut.StartAsync(games, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await writer.DidNotReceive().WriteAsync(Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Batch_ShouldCountEveryTiedWinner()
    {
        IPlayerClient playerClient = Substitute.For<IPlayerClient>();
        playerClient.RequestPlayAsync(Arg.Any<int>(), Arg.Any<PlayerPlayRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new PlayerPlayResponse(call.Arg<PlayerPlayRequest>().Turn == 1 ? 0 : -1, string.Empty)));
        IDominoRandomizer randomizer = Substitute.For<IDominoRandomizer>();
        randomizer.Shuffle(Arg.Any<IReadOnlyList<int>>()).Returns(new int[]
        {
            0, 1, 2, 3, 4, 5, 6,
            7, 8, 9, 17, 23, 26, 27,
            24, 25, 13, 10, 11, 12, 16,
            14, 15, 18, 19, 20, 21, 22
        });
        IGameStatisticsWriter writer = Substitute.For<IGameStatisticsWriter>();
        DominoBatchService sut = new(playerClient, randomizer, writer);

        await sut.StartAsync(2, CancellationToken.None);

        await writer.Received(1).WriteAsync(Arg.Is<IReadOnlyList<int>>(wins => wins.SequenceEqual(new[] { 0, 2, 2, 2 })), CancellationToken.None);
    }

    [Fact]
    public async Task Batch_ShouldRejectOverlapAndAllowNewRunAfterCancellation()
    {
        IPlayerClient playerClient = Substitute.For<IPlayerClient>();
        TaskCompletionSource<PlayerPlayResponse> pendingPlay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        playerClient.RequestPlayAsync(Arg.Any<int>(), Arg.Any<PlayerPlayRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => pendingPlay.Task.WaitAsync(call.Arg<CancellationToken>()));
        IGameStatisticsWriter writer = Substitute.For<IGameStatisticsWriter>();
        DominoBatchService sut = new(playerClient, new OrderedRandomizer(), writer);
        using CancellationTokenSource cancellation = new();
        Task runningBatch = sut.StartAsync(2, cancellation.Token);

        Func<Task> overlappingBatch = () => sut.StartAsync(1, CancellationToken.None);
        await overlappingBatch.Should().ThrowAsync<InvalidOperationException>();
        cancellation.Cancel();
        Func<Task> cancelledBatch = () => runningBatch;
        await cancelledBatch.Should().ThrowAsync<OperationCanceledException>();
        await writer.DidNotReceive().WriteAsync(Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>());

        playerClient.RequestPlayAsync(Arg.Any<int>(), Arg.Any<PlayerPlayRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerPlayResponse(-1, string.Empty)));
        await sut.StartAsync(1, CancellationToken.None);

        await writer.Received(1).WriteAsync(Arg.Is<IReadOnlyList<int>>(wins => wins.All(count => count == 0)), CancellationToken.None);
    }

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
        TurnDecision timeout = eventSink.Decisions.Single(decision => decision.Turn == 2);
        timeout.Player.Should().Be("p1");
        timeout.Tile.Should().BeNull();
        timeout.Accepted.Should().BeFalse();
        timeout.Reason.Should().Be("player_failure");
        timeout.PlayerTiles.Should().Equal(playerClient.Requests[1].YourTiles);
        TurnDecision first = eventSink.Decisions[0];
        first.Table.Should().BeEmpty();
        first.PlayerTiles.Should().Equal(playerClient.Requests[0].YourTiles).And.Contain(first.Tile!.Value);
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
        eventSink.GamesStarted.Should().Be(1);

        Func<Task> duplicateStart = () => sut.StartAsync(CancellationToken.None);
        await duplicateStart.Should().ThrowAsync<InvalidOperationException>();
        eventSink.GamesStarted.Should().Be(1);
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
        public int GamesStarted { get; private set; }

        public void BeginGame() => GamesStarted++;

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