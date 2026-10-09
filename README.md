# Domino Master API

An in-memory Master API that coordinates a four-player Domino game. The Master
API deals the complete double-six set, sends turn state to players, validates
plays, disqualifies cheating or unavailable players, detects blocked games, and
notifies players when the game ends.

## Requirements

- .NET 10 SDK
- Four player APIs reachable by HTTP

The application intentionally has no persistence in this version. Restarting
the process clears the active game.

## Player API contract

Configure four player base URLs in `src/WebApi/appsettings.json`. The Master
API calls the corresponding URL with these routes.

Player APIs are stateless. The Master API no longer calls a `/tiles` endpoint.
Tile values are indexes into the known double-six list, from `0` through `27`.

### `POST /play`

The request contains the current table, the played-tile history for each
player, the player whose turn it is, and that player's current tiles. A player
responds with a tile index and `head` or `tail` when the table is non-empty. A
player that cannot play responds with `tile: -1`.

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
  "to_play": "p1",
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

`your_tiles` contains the recipient's remaining tiles at game end. It is empty
for a player who wins by playing their last tile; blocked-game winners retain
their remaining tiles.

Player callback timeouts and transport failures disqualify the affected
player. Disqualified players are represented as `null` in later play state and
do not receive the final notification.

## Ordered game logs

The Master assigns a UUID `game_id` at game start. `turn` starts at 1 and
increments before each requested play, including passes, rejected moves, and
timeouts. Skipped disqualified players do not consume a turn. Both `/play` and
`/end` include these fields; `/end` uses the last requested turn number.

Use the Master's structured logs as the source of validated outcomes, not the
display order of Docker Compose's merged Player logs. JSON console logs contain
event fields under `State`:

- Event ID `1001` (`TurnDecision`): `game_id`, `turn`, `player`, `tile`,
  `position`, `accepted`, and `reason`. Emitted after validation, including
  accepted passes. Callback failures have `accepted: false`, `tile: null`, and
  `reason: "player_failure"`.
- Event ID `1002` (`GameResult`): `game_id`, the last `turn`, `player`, `win`,
  `disqualified`, and remaining `points`. Recorded for all four players,
  including disqualified players, independently of callback delivery.

Order turn decisions by `(game_id, turn)`. Final-result events share the last
turn and are distinguished by event ID and player. Logging is not durable
storage; interrupted or cancelled games may have incomplete event sequences.

## Master API

### `POST /start`

Starts the game using the four configured player endpoints. The request runs
the game loop and returns `202 Accepted` after final notifications have been
attempted. This version does not implement player subscription or sessions.

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
```