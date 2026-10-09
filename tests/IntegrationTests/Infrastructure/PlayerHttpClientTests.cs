using System.Net;
using System.Text.Json;
using Application.Domino;
using FluentAssertions;
using Infrastructure.Domino;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace IntegrationTests.Infrastructure;

public sealed class PlayerHttpClientTests
{
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

        await sut.SendGameEndAsync(2, new GameEndNotification(win, tiles), CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("http://player2/end"));
        handler.Method.Should().Be(HttpMethod.Post);
        using JsonDocument document = JsonDocument.Parse(handler.Body!);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("win", "your_tiles");
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
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}