using Application.Domino;

namespace Infrastructure.Domino;

public sealed class RandomDominoRandomizer : IDominoRandomizer
{
    public IReadOnlyList<int> Shuffle(IReadOnlyList<int> values)
    {
        int[] result = values.ToArray();
        for (int index = result.Length - 1; index > 0; index--)
        {
            int swapIndex = Random.Shared.Next(index + 1);
            (result[index], result[swapIndex]) = (result[swapIndex], result[index]);
        }

        return result;
    }
}