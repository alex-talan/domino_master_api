using Application.Domino;
using FluentAssertions;
using Infrastructure.Domino;
using Microsoft.Extensions.Logging;

namespace IntegrationTests.Infrastructure;

public sealed class LoggingGameEventSinkTests
{
    private static readonly Guid GameId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");

    [Fact]
    public async Task StatisticsWriter_ShouldReplaceOutputWithTeamTotals()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("domino-statistics-");
        string outputPath = Path.Combine(directory.FullName, "output.log");
        try
        {
            await File.WriteAllTextAsync(outputPath, "previous game output");
            GameStatisticsWriter sut = new(outputPath);

            await sut.WriteAsync([70, 30], CancellationToken.None);

            (await File.ReadAllLinesAsync(outputPath)).Should().Equal(
                "Team0 (Player1 and Player3) wins 70 games",
                "Team1 (Player2 and Player4) wins 30 games");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RecordTurn_ShouldLogPlayerStyleTurn_WhenMoveIsAccepted()
    {
        CapturingLogger logger = new();
        using GameFileLoggerProvider fileLoggerProvider = new();
        LoggingGameEventSink sut = new(logger, fileLoggerProvider);

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
        using GameFileLoggerProvider fileLoggerProvider = new();
        LoggingGameEventSink sut = new(logger, fileLoggerProvider);

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
        using GameFileLoggerProvider fileLoggerProvider = new();
        LoggingGameEventSink sut = new(logger, fileLoggerProvider);

        sut.RecordResult(new PlayerGameResult(GameId, 40, "p2", win, disqualified, tiles));

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Message.Should().StartWith(expected);
    }

    [Fact]
    public void BeginGame_ShouldOverwriteFileAndWriteOnlyGameMessages()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("domino-logs-");
        string outputPath = Path.Combine(directory.FullName, "output.log");
        try
        {
            File.WriteAllText(outputPath, "previous game output");
            CapturingLogger consoleLogger = new();
            using GameFileLoggerProvider fileLoggerProvider = new(outputPath);
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(consoleLogger);
                builder.AddProvider(fileLoggerProvider);
            });
            LoggingGameEventSink sut = new(loggerFactory.CreateLogger<LoggingGameEventSink>(), fileLoggerProvider);

            sut.BeginGame();
            File.ReadAllText(outputPath).Should().BeEmpty();
            sut.RecordTurn(new TurnDecision(GameId, 1, "p0", 25, "head", true, null, [], [25, 18]));
            sut.RecordTurn(new TurnDecision(GameId, 2, "p1", 3, "tail", false, "Incorrect tile.", [25], [3, 9]));
            sut.RecordResult(new PlayerGameResult(GameId, 2, "p0", true, false, []));
            loggerFactory.CreateLogger("Microsoft.AspNetCore").LogInformation(new EventId(1001), "Framework message");

            consoleLogger.Entries.Should().HaveCount(3);
            File.ReadAllLines(outputPath).Should().Equal(consoleLogger.Entries.Select(entry => entry.Message));

            sut.BeginGame();
            File.ReadAllText(outputPath).Should().BeEmpty();
            sut.RecordResult(new PlayerGameResult(Guid.NewGuid(), 1, "p1", false, true, [0, 7]));
            File.ReadAllLines(outputPath).Should().Equal(consoleLogger.Entries[^1].Message);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class CapturingLogger : ILogger<LoggingGameEventSink>, ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => categoryName == typeof(LoggingGameEventSink).FullName
            ? this
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
