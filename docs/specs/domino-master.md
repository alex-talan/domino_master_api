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

### Step 1.1: Prepare the game

The master application has a list of X endpoints (the addresses of all player APIs). In the next versions, we will implement a system to create player pools, championships, and more. For this version, we assume we have only 4 endpoints corresponding to the 4 player applications. These endpoints, for this version, will be configured in a configuration file.

The game will start once the master application receives a call to `POST /start` from an external API (it act as starting trigger).

## Step 2: Tile distribution

The master application will randomly distribute 7 tiles to each player (28 total = all tiles). The master application will keep these 4 lists in memory, because it will need them in every turn. 

**Example**:
The master application must define the following lists:
```csharp
int[] player_1 = [3, 14, 22, 0, 7, 19, 11];
int[] player_2 = [25, 8, 1, 16, 27, 4, 12];
int[] player_3 = [9, 21, 17, 20, 13, 26, 18];
int[] player_4 = [2, 10, 6, 23, 15, 24, 5];
```

These lists contain only the indices of the tiles in the main list `Tiles`, that is well known by all players. For example, the following tiles are assigned to the first player:

- 3: (0,3)
- 14: (2,3)
- 22: (4,4)
- 0: (0,0)
- 7: (1,1)
- 19: (3,4)
- 11: (1,5)

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
    "p3": [],
	"to_play": "p0",
	"your_tiles": [3, 14, 22, 0, 7, 19, 11]
}
```

This is the description of each field:
- The `table` field contains the list of played tiles.
- The `p0`, `p1`, `p2` and `p3` fields contain the list of tiles played by each player.
- `head` contains the available number at the **head** (front) of the table.
- `tail` contains the available number at the **tail** (back) of the table.
- `to_play` is the name of the player that must play in that turn (ex. `p1`. This example indicates that the current player has already played the tiles in the list `"p1"`).
- `your_tiles` is the set of tiles available to play

**EXAMPLE**:
Imagine the following table: `[6, 21, 19]`. The tiles are `(0,6)`, `(3,6)`, `(3,4)`. If we reorder the tiles to represent a compatible domino sequence, we obtain this:

`(0,6)-(6,3)-(3,4)`

Thus, the 6 of the first tile is compatible with the 6 of the second tile, and the 3 of the second tile is compatible with the 3 of the third tile. In this scenario, the **head** of the table is 0 (the first number of the sequence), and the **tail** is 4 (the last number of the sequence).

In the first call to the first player, of course, no player has played yet, so all lists are empty and `head` and `tail` are `null`. The first player is always `p0` and the list of player's tiles contains 7 tiles (`your_tiles`).

To send this information to player 1, the master application calls the endpoint `POST /play` with the previous payload. The player will respond with the tile they want to place and its position (`head` or `tail`).

Example:

```json
{
    "tile": 22,
    "position": "tail"
}
```

The first player played the tile `(4,4)`. Note that, in this case, the position is not relevant because the table is empty.

The game continues. The master application receives the play from player 1, performs some validations (we will see that later), and asks the second player to play:

```bash
POST /play
{
    "table": [22],
    "head": 4,
    "tail": 4,
    "p0": [22],
    "p1": [],
    "p2": [],
    "p3": [],
	"to_play": "p1",
	"your_tiles": [25, 8, 1, 16, 27, 4, 12]
}
```

Now player 2 (index=1) plays, sending the following response:

```json
{
    "tile": 4,
    "position": "tail"
}
```

Player 2 played the tile `(0,4)` at the back (`tail`).

The master application will validate the play according to the following rules:

- If the table is empty, the position is not relevant: the tile is added to the table, and the play is valid.
- If the table contains at least one tile, the position IS relevant, so the master application will verify whether the tile sent by the player contains the number assigned to the position they selected in the play. For this example, the player played the tile `(0,4)` at the tail. The tail of the game is 4 (`"tail":4`), and it is contained in the played tile, so the play is valid.

If the play is not validated, it is considered cheating, and the player is disqualified (e.g., `"p1": null`). The player 1 is no longer asked to play, and their tiles are (of course) out of play.

If the play is validated, the following payload is sent to the third player:

```bash
POST /play
{
    "table": [22, 4],
    "head": 4,
    "tail": 0,
    "p0": [22],
    "p1": [4],
    "p2": [],
    "p3": [],
	"to_play": "p2",
	"your_tiles": [9, 21, 17, 20, 13, 26, 18]
}
```

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
    "table": [22, 4],
    "head": 4,
    "tail": 0,
    "p0": [22],
    "p1": [4],
    "p2": [-1],
    "p3": [],
	"to_play": "p3",
	"your_tiles": [2, 10, 6, 23, 15, 24, 5]
}
```

**Important**: if a player sends a `-1` play, but he actually can play, it is considered cheating, and he is automatically disqualified. 

### Subsequent rounds
If we follow the example, it is fourth player's turn, so imagine he plays this:
```json
{
    "tile": 2,
    "position": "tail"
}
```

Then, the first round is finished and the following call is sent to the first player:
```bash
POST /play
{
    "table": [22, 4, 2],
    "head": 4,
    "tail": 2,
    "p0": [20],
    "p1": [5],
    "p2": [-1],
    "p3": [2],
	"to_play": "p0",
	"your_tiles": [3, 14, 0, 7, 19, 11]
}
```
Note that now, the current player's 1 tiles are updated: the tile 22 (already played) has been removed.

And the game continues: it is now second player's turn. And so on...

### Particular case: player timeout
If a player API times out, that player is automatically disqualified.

## Step 4: The end of the game

The game will end if one of the following events occurs:

### Blocked game

If none of the 4 players can play because they do not have tiles compatible with the head or the **tail** of the table, the game is blocked. The master application can verify this when 4 consecutive `-1` values are received from the players.

In that case, the master application selects the winner: the player with the fewest points in their tiles.

Example:

- Player 1's tiles: 16-(2,5), 24-(4,6). Total = 2+5+4+6 = 17 points
- Player 2's tiles: 6-(0,6), 7-(1,1). Total = 0+6+1+1 = 8 points
- Player 3's tiles: 3-(0,3). Total = 0+3 = 3 points → THE WINNER
- Player 4's tiles: 20-(3,5). Total = 3+5 = 8 points

The master application then sends a final call to all players, with the field `win` set to `true` if the player is the winner and `false` otherwise.

If two or more players have the same score,  the master application declares those players as winners.

Example:

```bash
POST /end
{
    "win": true
}
```

### A player finishes
The first player to play its last tile, becomes the winner. The other are declared losers.