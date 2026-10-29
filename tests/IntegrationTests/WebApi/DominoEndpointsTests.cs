using System.Net;
using Application.Domino;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IntegrationTests.WebApi;

public sealed class DominoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    private readonly WebApplicationFactory<Program> factory;

    public DominoEndpointsTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
    }

    [Theory]
    [InlineData("?games=0")]
    [InlineData("?games=-1")]
    [InlineData("?games=invalid")]
    public async Task PostStart_ShouldRejectInvalidGames(string query)
    {
        using HttpResponseMessage response = await client.PostAsync($"/start{query}", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("?games=3", 3)]
    public async Task PostStart_ShouldRunRequestedGamesWithoutPlayerSubscription(string query, int games)
    {
        StatelessPlayerClient player = new();
        RecordingStatisticsWriter writer = new();
        using WebApplicationFactory<Program> isolatedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPlayerClient>();
            services.RemoveAll<IGameStatisticsWriter>();
            services.AddSingleton<IPlayerClient>(player);
            services.AddSingleton<IGameStatisticsWriter>(writer);
        }));
        using HttpClient isolatedClient = isolatedFactory.CreateClient();

        using HttpResponseMessage response = await isolatedClient.PostAsync($"/start{query}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        player.GameIds.Should().HaveCount(games);
        writer.Writes.Should().Be(1);
        writer.Wins.Should().Equal(0, 0);
    }

    [Fact]
    public async Task GetStart_ShouldNotBeSupported()
    {
        HttpResponseMessage response = await client.GetAsync("/start");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    private sealed class StatelessPlayerClient : IPlayerClient
    {
        public HashSet<Guid> GameIds { get; } = [];

        public Task<PlayerPlayResponse> RequestPlayAsync(int playerIndex, PlayerPlayRequest request, CancellationToken cancellationToken)
        {
            GameIds.Add(request.GameId);
            return Task.FromResult(new PlayerPlayResponse(-1, string.Empty));
        }

        public Task SendGameEndAsync(int playerIndex, GameEndNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingStatisticsWriter : IGameStatisticsWriter
    {
        public int Writes { get; private set; }

        public IReadOnlyList<int> Wins { get; private set; } = [];

        public Task WriteAsync(IReadOnlyList<int> wins, CancellationToken cancellationToken)
        {
            Writes++;
            Wins = wins.ToArray();
            return Task.CompletedTask;
        }
    }
}