# Domino Master API — Functional Specification

## Objective

Implement a Master API responsible for orchestrating a four-player Domino game.

The system consists of:
- one (1) Master API;
- four (4) Player APIs.

The Master API is responsible for:
- registering/identifying players;
- distributing tiles;
- managing turns;
- validating plays;
- detecting cheating;
- detecting blocked games;
- determining the winner;
- notifying players when the game ends.

## Step 1. Master application.

This is how we define the **tiles** of the game:

```csharp
public static readonly (int, int)[] Tiles =
[
    (0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (0, 5), (0, 6),
    (1, 1), (1, 2), (1, 3), (1, 4), (1, 5), (1, 6),
    (2, 2), (2, 3), (2, 4), (2, 5), (2, 6),
    (3, 3), (3, 4), (3, 5), (3, 6),
    (4, 4), (4, 5), (4, 6),
    (5, 5), (5, 6),
    (6, 6)
];
```

This tile order **is known by ALL players**.

### Step 1.1: Test application: player subscription

This is a version of the master API that will be developed so that players can test their solutions during development. The player sends an API call `GET /start` to the master application, and the latter responds with a `session_id` and an index (0 to 3).

Example of the master's response:

```json
{
    "session_id": f47ac10b-58cc-4372-a567-0e02b2c3d479,
    "index": 3
}
```

> - [ ] Not sure the `session_id` is relevant.

### Step 1.2: Prepare the game

The master application has a list of X endpoints (the addresses of all player APIs). In the next versions, we will implement a system to create player pools, championships, and more. For this version, we assume we have only 4 endpoints corresponding to the 4 player applications. These endpoints, for this version, will be configured in a configuration file.

The game will start once the master application receives a call to `POST /start`.

## Step 2: Tile distribution

The master application will randomly distribute 7 tiles to each player (28 total = all tiles). To do that, the master application will call all players using the endpoint `POST /tiles`, sending a list of 7 randomly selected tiles.

Example:

```json
{
    "tiles": [3, 8, 13, 21, 20, 5, 19]
}
```

As you can see, the master application sends the indices of the tiles in the list of tiles that is well known by all players. For this example, the application sends the following tiles:

- 3: (0,3)
- 8: (1,2)
- 13: (2,2)
- 21: (3,6)
- 20: (3,5)
- 5: (0,5)
- 19: (3,4)

## Step 3: The game

The master application starts the game by sending the state of the table to the first player (index=0):

```json
{
    "table": [],
    "head": null,
    "tail": null,
    "p0": [],
    "p1": [],
    "p2": [],
    "p3": []
}
```

The `table` field contains the list of played tiles. The `pX` fields contain the list of tiles played by each player. `head` contains the available number at the **head** (front) of the table. `tail` contains the available number at the **tail** (back) of the table.

For example, imagine the following table: `[6, 21, 19]`. The tiles are `(0,6)`, `(3,6)`, `(3,4)`. If we reorder the tiles to represent a compatible domino sequence, we obtain this:

`(0,6)-(6,3)-(3,4)`

Thus, the 6 of the first tile is compatible with the 6 of the second tile, and the 3 of the second tile is compatible with the 3 of the third tile. In this scenario, the **head** of the table is 0 (the first number of the sequence), and the **tail** is 4 (the last number of the sequence).

In the first call to the first player, of course, no player has played yet, so all lists are empty and `head` and `tail` are `null`.

To send this information to player 1, the master application calls the endpoint `POST /play` with the previous payload. The player will respond with the tile they want to place and its position (`head` or `tail`).

Example:

```json
{
    "tile": 20,
    "position": "tail"
}
```

The first player played the tile `(3,5)`. Note that, in this case, the position is not relevant because the table is empty.

The game continues. The master application receives the play from player 1, performs some validations (we will see that later), and asks the second player to play:

```bash
POST /play
{
    "table": [20],
    "head": 3,
    "tail": 5,
    "p0": [20],
    "p1": [],
    "p2": [],
    "p3": []
}
```

Now player 2 (index=1) plays, sending the following response:

```json
{
    "tile": 5,
    "position": "tail"
}
```

Player 2 played the tile `(0,5)` at the back (`tail`).

The master application will validate the play according to the following rules:

- If the table is empty, the position is not relevant: the tile is added to the table, and the play is valid.
- If the table contains at least one tile, the position IS relevant, so the master application will verify whether the tile sent by the player contains the number assigned to the position they selected in the play. For this example, the player played the tile `(0,5)` at the tail. The tail of the game is 5 (`"tail"=5`), and it is contained in the played tile, so the play is valid.

If the play is not validated, it is considered cheating, and the player is disqualified (e.g., `"p1": null`).

If the play is validated, the following payload is sent to the third player:

```bash
POST /play
{
    "table": [20, 5],
    "head": 3,
    "tail": 0,
    "p0": [20],
    "p1": [5],
    "p2": [],
    "p3": []
}
```

...and so on.

### Particular case: the player has no tile to play

Imagine that the third player cannot play at either the head or the **tail**. In that case, the player "passes" their turn by sending `-1` as the tile (the position is not relevant).

Example:

```json
{
    "tile": -1,
    "position": ""
}
```

In this case, the fourth player is called with the following payload (`head` and `tail` remain the same):

```bash
POST /play
{
    "table": [20, 5],
    "head": 3,
    "tail": 0,
    "p0": [20],
    "p1": [5],
    "p2": [-1],
    "p3": []
}
```

## Step 4: The end of the game

The game will end if one of the following events occurs:

### Blocked game

If none of the 4 players can play because they do not have tiles compatible with the head or the **tail** of the table, the game is blocked. The master application can verify this when 4 consecutive `-1` values are received from the players.

In that case, the master application selects the winner: the player with the fewest points in their tiles.

Example:

- Player 1's tiles: 16-(2,5), 24-(4,6). Total = 2+5+4+6 = 17 points
- Player 2's tiles: 6-(0,6), 7-(1,1). Total = 0+6+1+1 = 8 points
- Player 3's tiles: 3-(0,3). Total = 0+3 = 3 points → THE WINNER
- Player 4's tiles: 20-(3,5). Total = 3+5 = 3 points

The master application then sends a final call to all players, with the field `win` set to `true` if the player is the winner and `false` otherwise.

Example:

```bash
GET /end
{
    "win": true
}
```

