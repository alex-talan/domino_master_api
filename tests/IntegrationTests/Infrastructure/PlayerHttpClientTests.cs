using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Domino;
using FluentAssertions;
using Infrastructure.Domino;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace IntegrationTests.Infrastructure;

public sealed class PlayerHttpClientTests
{
    [Fact]
    public async Task RequestPlayAsync_ShouldSendGameIdAndTurnWithState()
    {
        Guid gameId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        PlayerHttpClient sut = new(httpClient, Options.Create(new DominoOptions
        {
            PlayerEndpoints = ["http://player0", "http://player1", "http://player2", "http://player3"]
        }));
        PlayerPlayRequest request = new([], null, null, [], [], [], [], "p0", [0, 1, 2, 3, 4, 5, 6], gameId, 1);

        PlayerPlayResponse response = await sut.RequestPlayAsync(0, request, CancellationToken.None);

        response.Should().Be(new PlayerPlayResponse(0, "tail"));
        handler.RequestUri.Should().Be(new Uri("http://player0/play"));
        handler.Method.Should().Be(HttpMethod.Post);
        using JsonDocument document = JsonDocument.Parse(handler.Body!);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("table", "head", "tail", "p0", "p1", "p2", "p3", "to_play", "your_tiles", "game_id", "turn");
        document.RootElement.GetProperty("game_id").GetGuid().Should().Be(gameId);
        document.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("your_tiles").EnumerateArray()
            .Select(tile => tile.GetInt32()).Should().Equal(request.YourTiles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendGameEndAsync_ShouldSendFinalTilesWithExpectedJsonNames(bool win)
    {
        int[] tiles = win ? [] : [0, 14, 19];
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        PlayerHttpClient sut = new(httpClient, Options.Create(new DominoOptions
        {
            PlayerEndpoints = ["http://player0", "http://player1", "http://player2", "http://player3"]
        }));

        Guid gameId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
        await sut.SendGameEndAsync(2, new GameEndNotification(win, tiles, gameId, 25), CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("http://player2/end"));
        handler.Method.Should().Be(HttpMethod.Post);
        using JsonDocument document = JsonDocument.Parse(handler.Body!);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("win", "your_tiles", "game_id", "turn");
        document.RootElement.GetProperty("game_id").GetGuid().Should().Be(gameId);
        document.RootElement.GetProperty("turn").GetInt32().Should().Be(25);
        document.RootElement.GetProperty("win").GetBoolean().Should().Be(win);
        document.RootElement.GetProperty("your_tiles").EnumerateArray()
            .Select(tile => tile.GetInt32()).Should().Equal(tiles);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { tile = 0, position = "tail" })
            };
        }
    }
}