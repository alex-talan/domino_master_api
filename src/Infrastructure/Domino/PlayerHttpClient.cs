using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Domino;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Domino;

public sealed class PlayerHttpClient(HttpClient httpClient, IOptions<DominoOptions> options) : IPlayerClient
{
    private readonly string[] playerEndpoints = options.Value.PlayerEndpoints;

    public async Task<PlayerPlayResponse> RequestPlayAsync(int playerIndex, PlayerPlayRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await PostAsync(playerIndex, "play", new PlayerPlayPayload(request), cancellationToken);
        try
        {
            PlayerPlayResponsePayload? payload = await response.Content.ReadFromJsonAsync<PlayerPlayResponsePayload>(cancellationToken);
            return payload is null
                ? throw new HttpRequestException("The player returned an empty play response.")
                : new PlayerPlayResponse(payload.Tile, payload.Position);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("The player returned an invalid play response.", exception);
        }
    }

    public async Task SendGameEndAsync(int playerIndex, GameEndNotification notification, CancellationToken cancellationToken)
    {
        using HttpResponseMessage _ = await PostAsync(playerIndex, "end", new GameEndPayload(notification.Win, notification.YourTiles, notification.GameId, notification.Turn), cancellationToken);
    }

    private async Task<HttpResponseMessage> PostAsync(int playerIndex, string path, object payload, CancellationToken cancellationToken)
    {
        if (playerIndex < 0 || playerIndex >= playerEndpoints.Length)
        {
            throw new InvalidOperationException($"No endpoint is configured for player {playerIndex}.");
        }

        HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"{playerEndpoints[playerIndex].TrimEnd('/')}/{path}",
            payload,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            int statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"Player {playerIndex} returned HTTP {statusCode}.");
        }

        return response;
    }

    private sealed record PlayerPlayPayload(
        [property: JsonPropertyName("table")] IReadOnlyList<int> Table,
        [property: JsonPropertyName("head")] int? Head,
        [property: JsonPropertyName("tail")] int? Tail,
        [property: JsonPropertyName("p0")] IReadOnlyList<int>? Player0,
        [property: JsonPropertyName("p1")] IReadOnlyList<int>? Player1,
        [property: JsonPropertyName("p2")] IReadOnlyList<int>? Player2,
        [property: JsonPropertyName("p3")] IReadOnlyList<int>? Player3,
        [property: JsonPropertyName("to_play")] string ToPlay,
        [property: JsonPropertyName("your_tiles")] IReadOnlyList<int> YourTiles,
        [property: JsonPropertyName("game_id")] Guid GameId,
        [property: JsonPropertyName("turn")] int Turn)
    {
        public PlayerPlayPayload(PlayerPlayRequest request)
            : this(request.Table, request.Head, request.Tail, request.Player0, request.Player1, request.Player2, request.Player3, request.ToPlay, request.YourTiles, request.GameId, request.Turn)
        {
        }
    }

    private sealed record GameEndPayload(
        [property: JsonPropertyName("win")] bool Win,
        [property: JsonPropertyName("your_tiles")] IReadOnlyList<int> YourTiles,
        [property: JsonPropertyName("game_id")] Guid GameId,
        [property: JsonPropertyName("turn")] int Turn);

    private sealed record PlayerPlayResponsePayload(
        [property: JsonPropertyName("tile")] int Tile,
        [property: JsonPropertyName("position")] string? Position);
}