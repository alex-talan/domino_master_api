using Application.Domino;
using FluentAssertions;
using Infrastructure.Domino;
using Microsoft.Extensions.Logging;

namespace IntegrationTests.Infrastructure;

public sealed class LoggingGameEventSinkTests
{
    private static readonly Guid GameId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");

    [Fact]
    public void RecordTurn_ShouldLogPlayerStyleTurn_WhenMoveIsAccepted()
    {
        CapturingLogger logger = new();
        LoggingGameEventSink sut = new(logger);

        sut.RecordTurn(new TurnDecision(GameId, 1, "p0", 25, "head", true, null, [], [25, 18, 20, 22, 6, 14, 9]));

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Information);
        logger.Entries[0].Message.Should().StartWith(
            "player1 | TURN:1 - TABLE:[] - My Tiles:[25, 18, 20, 22, 6, 14, 9] - My Move:(25, head)");
    }

    [Fact]
    public void RecordTurn_ShouldLogWarningWithMove_WhenMoveIsIncorrect()
    {
        CapturingLogger logger = new();
        LoggingGameEventSink sut = new(logger);

        sut.RecordTurn(new TurnDecision(GameId, 6, "p2", 3, "tail", false, "The tile does not match the table tail.", [22, 4], [3, 9]));

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
        logger.Entries[0].Message.Should().StartWith(
            "player3 | TURN:6 - TABLE:[22, 4] - My Tiles:[3, 9] - INCORRECT MOVE:(3, tail) - The tile does not match the table tail. - DISQUALIFIED");
    }

    [Theory]
    [InlineData(true, false, new int[0], "player3 | WINNER | Final Tiles:[]")]
    [InlineData(false, false, new[] { 22 }, "player3 | Did not win | Final Tiles:[22]")]
    [InlineData(false, true, new[] { 0, 7 }, "player3 | Disqualified | Final Tiles:[0, 7]")]
    public void RecordResult_ShouldLogOutcomeAndFinalTiles(bool win, bool disqualified, int[] tiles, string expected)
    {
        CapturingLogger logger = new();
        LoggingGameEventSink sut = new(logger);

        sut.RecordResult(new PlayerGameResult(GameId, 40, "p2", win, disqualified, tiles));

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Message.Should().StartWith(expected);
    }

    private sealed class CapturingLogger : ILogger<LoggingGameEventSink>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
