# Domino Master API

An in-memory Master API that coordinates a four-player Domino game. The Master API deals the complete double-six set, sends turn state to players, validates plays, disqualifies cheating or unavailable players, detects blocked games, and notifies players when the game ends.

## Requirements

- .NET 10 SDK
- Four player APIs reachable by HTTP

The application intentionally has no persistence in this version. Restarting the process clears the active game.

## Player API contract

Configure four player base URLs in `src/WebApi/appsettings.json`. The Master API calls the corresponding URL with these routes.

Player APIs are stateless. The Master API no longer calls a `/tiles` endpoint. Tile values are indexes into the known double-six list, from `0` through `27`.

### `POST /play`

The request contains the current table, the played-tile history for each player, the player whose turn it is, and that player's current tiles. A player responds with a tile index and `head` or `tail` when the table is non-empty. A player that cannot play responds with `tile: -1`.

```json
{
  "game_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "turn": 3,
  "table": [20, 5],
  "head": 3,
  "tail": 0,
  "p0": [20],
  "p1": [5],
  "p2": [],
  "p3": [],
  "to_play": "p2",
  "your_tiles": [25, 8, 1, 16, 27, 4, 12]
}
```

```json
{
  "tile": 5,
  "position": "tail"
}
```

### `POST /end`

```json
{
  "game_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "turn": 25,
  "win": false,
  "your_tiles": [0, 14, 19]
}
```

`your_tiles` contains the recipient's remaining tiles at game end. It is empty for a player who wins by playing their last tile; blocked-game winners retain their remaining tiles.

Player callback timeouts and transport failures disqualify the affected player. Disqualified players are represented as `null` in later play state and do not receive the final notification.

## Batch statistics

`POST /start?games=100` runs 100 games sequentially. Each game has fresh hands,
table state, disqualification flags, and a new game ID. The Master retains only
four win counters across games, not turn histories or per-game result records.

After all games finish, `output.log` in the application's current working
directory is overwritten with the win totals. The directory must be writable.
For example:

```text
Player1 wins 20 games
Player2 wins 70 games
Player3 wins 10 games
Player4 wins 0 games
```

Player1 through Player4 correspond to protocol players p0 through p3. Every
tied winner receives one win, so the totals may exceed the number of games.
Games where everyone is disqualified have no winner. Batch runs do not write
individual turn or result logs; framework console logging remains JSON.

The Master assigns a UUID `game_id` at game start. `turn` starts at 1 and increments before each requested play, including passes, rejected moves, and timeouts. Skipped disqualified players do not consume a turn. Both `/play` and `/end` include these fields; `/end` uses the last requested turn number.

## Master API

### `POST /start`

Starts a sequential batch using the four configured player endpoints. The
optional `games` query parameter is a positive integer and defaults to 1. The
request returns `202 Accepted` after all games, final notifications, and the
statistics write complete. Invalid counts return `400`; a concurrent batch
returns `409`. A subsequent request starts a new batch with new totals.
Cancellation stops the batch without publishing a completed summary. This
version does not implement player subscription or sessions.

## Game rules

- The 28 known tiles are shuffled and dealt as seven tiles per player.
- A tile must belong to the responding player's hand.
- On a non-empty table, the selected position must match the tile.
- Passing while holding a playable tile is cheating and disqualifies the player.
- Four consecutive valid passes block the game.
- In a normal game, the player who plays their last tile wins.
- In a blocked game, every non-disqualified player with the lowest remaining
  point total is declared a winner. Ties therefore produce multiple winners.

## Configuration

```json
{
  "Domino": {
    "PlayerEndpoints": [
      "http://localhost:5001",
      "http://localhost:5002",
      "http://localhost:5003",
      "http://localhost:5004"
    ],
    "PlayerTimeoutSeconds": 10
  }
}
```

Environment variables use the standard double-underscore form, for example
`Domino__PlayerTimeoutSeconds`.

## Run and verify

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/WebApi
```

OpenAPI is available in Development at `/openapi/v1.json`; health checks are
available at `/health`.

# Start the game 
```bash
curl -X POST http://localhost:8000/start
curl -X POST "http://localhost:8000/start?games=100"
```