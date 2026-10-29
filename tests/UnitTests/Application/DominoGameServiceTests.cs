using Application.Domino;
using FluentAssertions;
using NSubstitute;

namespace UnitTests.Application;

public sealed class DominoGameServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task StartAsync_ShouldAssignEntireDoubleSixHandToPlayerZero(int doubleSixHand)
    {
        List<int> deal = Enumerable.Range(0, 27).ToList();
        deal.Insert(doubleSixHand * 7, 27);
        IDominoRandomizer randomizer = Substitute.For<IDominoRandomizer>();
        randomizer.Shuffle(Arg.Any<IReadOnlyList<int>>()).Returns(deal);
        IPlayerClient playerClient = Substitute.For<IPlayerClient>();
        List<PlayerPlayRequest> requests = [];
        playerClient.RequestPlayAsync(Arg.Any<int>(), Arg.Any<PlayerPlayRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requests.Add(call.Arg<PlayerPlayRequest>());
                return Task.FromResult(new PlayerPlayResponse(-1, string.Empty));
            });

        await new DominoGameService(playerClient, randomizer, new RecordingGameEventSink()).StartAsync(CancellationToken.None);

        requests[0].ToPlay.Should().Be("p0");
        requests[0].YourTiles.Should().Equal(deal.Skip(doubleSixHand * 7).Take(7));
        requests.SelectMany(request => request.YourTiles).Should().BeEquivalentTo(Enumerable.Range(0, 28));
        requests[doubleSixHand].YourTiles.Should().Equal(deal.Take(7));
    }

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
        await writer.Received(1).WriteAsync(Arg.Is<IReadOnlyList<int>>(wins => wins.SequenceEqual(new[] { 0, 3 })), CancellationToken.None);

        await sut.StartAsync(1, CancellationToken.None);

        await writer.Received(1).WriteAsync(Arg.Is<IReadOnlyList<int>>(wins => wins.SequenceEqual(new[] { 0, 1 })), CancellationToken.None);
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
    public async Task Batch_ShouldCountBothBlockerTeammatesOnTie()
    {
        RecordingPlayerClient playerClient = new(timeoutPlayerOne: false);
        IDominoRandomizer randomizer = Substitute.For<IDominoRandomizer>();
        randomizer.Shuffle(Arg.Any<IReadOnlyList<int>>()).Returns(new int[]
        {
            11, 23, 10, 9, 26, 2, 8,
            3, 5, 27, 0, 12, 22, 16,
            20, 14, 18, 4, 15, 6, 24,
            13, 7, 17, 1, 21, 19, 25
        });
        IGameStatisticsWriter writer = Substitute.For<IGameStatisticsWriter>();
        DominoBatchService sut = new(playerClient, randomizer, writer);

        await sut.StartAsync(2, CancellationToken.None);

        await writer.Received(1).WriteAsync(Arg.Is<IReadOnlyList<int>>(wins => wins.SequenceEqual(new[] { 2, 0 })), CancellationToken.None);
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
        playerClient.GameEndNotifications.Single(notification => notification.PlayerIndex == 3).Notification.Win.Should().BeTrue();
        playerClient.GameEndNotifications.Where(notification => notification.PlayerIndex != 3).Should().AllSatisfy(notification => notification.Notification.Win.Should().BeFalse());
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
    [InlineData(28)]
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

    [Fact]
    public async Task StartAsync_ShouldRejectOpeningTileOtherThanDoubleSix()
    {
        IPlayerClient playerClient = Substitute.For<IPlayerClient>();
        playerClient.RequestPlayAsync(Arg.Any<int>(), Arg.Any<PlayerPlayRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerPlayResponse(21, "tail")));
        RecordingGameEventSink eventSink = new();

        await new DominoGameService(playerClient, new OrderedRandomizer(), eventSink).StartAsync(CancellationToken.None);

        eventSink.Decisions[0].Accepted.Should().BeFalse();
        eventSink.Decisions[0].Reason.Should().Be("The opening tile must be [6|6].");
        eventSink.Results.Should().OnlyContain(result => result.Disqualified && !result.Win);
    }

    [Fact]
    public async Task StartAsync_ShouldAwardNormalWinToFinisherAndPartner()
    {
        RecordingPlayerClient playerClient = new(timeoutPlayerOne: false);
        RecordingGameEventSink eventSink = new();
        IDominoRandomizer randomizer = Substitute.For<IDominoRandomizer>();
        randomizer.Shuffle(Arg.Any<IReadOnlyList<int>>()).Returns(new int[]
        {
            15, 23, 4, 9, 10, 19, 7,
            11, 13, 25, 3, 5, 12, 18,
            27, 24, 0, 2, 20, 1, 21,
            16, 6, 26, 8, 14, 22, 17
        });

        await new DominoGameService(playerClient, randomizer, eventSink).StartAsync(CancellationToken.None);

        eventSink.Decisions[0].Tile.Should().Be(27);
        eventSink.Decisions.Should().OnlyContain(decision => decision.Accepted);
        eventSink.Results.Where(result => result.Win).Select(result => result.Player).Should().Equal("p0", "p2");
        playerClient.GameEndNotifications.Single(result => result.PlayerIndex == 0).Notification.YourTiles.Should().BeEmpty();
        playerClient.GameEndNotifications.Single(result => result.PlayerIndex == 2).Notification.YourTiles.Should().NotBeEmpty();
        playerClient.GameEndNotifications.Should().AllSatisfy(result => result.Notification.Win.Should().Be(result.PlayerIndex % 2 == 0));
    }

    [Theory]
    [InlineData(new int[] { 20, 21, 2, 8, 13, 23, 27, 4, 14, 25, 5, 0, 1, 3, 11, 22, 17, 24, 9, 16, 15, 26, 19, 12, 18, 10, 6, 7 }, 1, 1)]
    [InlineData(new int[] { 16, 7, 5, 27, 19, 24, 8, 21, 22, 13, 23, 4, 1, 18, 17, 26, 2, 25, 10, 12, 20, 0, 11, 3, 15, 14, 6, 9 }, 2, 3)]
    [InlineData(new int[] { 11, 23, 10, 9, 26, 2, 8, 3, 5, 27, 0, 12, 22, 16, 20, 14, 18, 4, 15, 6, 24, 13, 7, 17, 1, 21, 19, 25 }, 0, 0)]
    [InlineData(new int[] { 4, 5, 24, 25, 27, 10, 20, 26, 12, 3, 23, 7, 8, 17, 15, 16, 13, 2, 11, 22, 6, 9, 14, 18, 0, 21, 19, 1 }, 3, 3)]
    public async Task StartAsync_ShouldCompareOnlyBlockerAndNextPlayer(int[] deal, int blocker, int winner)
    {
        RecordingPlayerClient playerClient = new(timeoutPlayerOne: false);
        RecordingGameEventSink eventSink = new();
        IDominoRandomizer randomizer = Substitute.For<IDominoRandomizer>();
        randomizer.Shuffle(Arg.Any<IReadOnlyList<int>>()).Returns(deal);

        await new DominoGameService(playerClient, randomizer, eventSink).StartAsync(CancellationToken.None);

        eventSink.Decisions.Should().OnlyContain(decision => decision.Accepted);
        eventSink.Decisions.Last(decision => decision.Tile != -1).Player.Should().Be($"p{blocker}");
        eventSink.Decisions.TakeLast(4).Should().OnlyContain(decision => decision.Tile == -1);
        eventSink.Results.Should().OnlyContain(result => !result.Disqualified && result.YourTiles.Count > 0);
        eventSink.Results.Where(result => result.Win).Select(result => result.Player)
            .Should().BeEquivalentTo(new[] { $"p{winner}", $"p{(winner + 2) % 4}" });
        playerClient.GameEndNotifications.Should().HaveCount(4).And.AllSatisfy(result =>
            result.Notification.Win.Should().Be(result.PlayerIndex % 2 == winner % 2));
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

    private sealed class RecordingPlayerClient(bool timeoutPlayerOne = true) : IPlayerClient
    {
        public List<PlayerPlayRequest> Requests { get; } = [];

        public Dictionary<int, IReadOnlyList<int>> ExpectedRemainingTiles { get; } = [];

        public List<(int PlayerIndex, GameEndNotification Notification)> GameEndNotifications { get; } = [];

        public bool SawDisqualifiedPlayerState { get; private set; }

        public bool SawCurrentTurnAndHand { get; private set; }

        public Task<PlayerPlayResponse> RequestPlayAsync(int playerIndex, PlayerPlayRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (timeoutPlayerOne && playerIndex == 1)
            {
                throw new TimeoutException();
            }

            SawDisqualifiedPlayerState |= request.Player1 is null;
            SawCurrentTurnAndHand |= request.ToPlay == $"p{playerIndex}" && request.YourTiles.Count > 0;
            HashSet<int> tiles = request.YourTiles.ToHashSet();
            ExpectedRemainingTiles[playerIndex] = tiles.ToArray();
            int? selectedTile = request.Head is null
                ? 27
                : tiles.Where(tile => Domain.Domino.DominoTile.All[tile].Contains(request.Head.Value) || Domain.Domino.DominoTile.All[tile].Contains(request.Tail!.Value))
                    .Select(tile => (int?)tile).FirstOrDefault();

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