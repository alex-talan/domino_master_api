namespace Domain.Domino;

public readonly record struct DominoTile(int First, int Second)
{
    public int Points => First + Second;

    public bool Contains(int value) => First == value || Second == value;

    public int OtherSide(int value)
    {
        if (!Contains(value))
        {
            throw new ArgumentException($"Tile ({First},{Second}) does not contain {value}.", nameof(value));
        }

        return First == value ? Second : First;
    }

    public static IReadOnlyList<DominoTile> All { get; } =
    [
        new(0, 0), new(0, 1), new(0, 2), new(0, 3), new(0, 4), new(0, 5), new(0, 6),
        new(1, 1), new(1, 2), new(1, 3), new(1, 4), new(1, 5), new(1, 6),
        new(2, 2), new(2, 3), new(2, 4), new(2, 5), new(2, 6),
        new(3, 3), new(3, 4), new(3, 5), new(3, 6),
        new(4, 4), new(4, 5), new(4, 6),
        new(5, 5), new(5, 6),
        new(6, 6)
    ];
}