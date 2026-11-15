using System.Globalization;
using System.Numerics;
using System.Text;

namespace ProgVorazCs.Core;

public static class ChangeCalculator
{
    public static bool ComputeUnlimited(BigInteger amount, IReadOnlyList<BigInteger> denominations, out List<BigInteger> result)
    {
        var answer = EnhancedChangeEngine.Compute(amount, denominations);
        result = answer?.Counts.ToList() ?? [];
        return answer is not null;
    }

    public static bool ComputeLimited(
        BigInteger amount,
        IReadOnlyList<BigInteger> denominations,
        IReadOnlyList<BigInteger> stock,
        out List<BigInteger> result,
        out List<BigInteger> updatedStock)
    {
        var answer = EnhancedChangeEngine.Compute(amount, denominations, stock);
        result = answer?.Counts.ToList() ?? [];
        updatedStock = answer is null ? stock.ToList() : stock.Zip(answer.Counts, (a,b) => a-b).ToList();
        return answer is not null;
    }

    private static bool ComputeOptimal(BigInteger amount, IReadOnlyList<BigInteger> denominations,
        IReadOnlyList<BigInteger>? stock, int maxCoins, int? minCoins,
        out List<BigInteger> result, out List<BigInteger> updatedStock)
    {
        result = Enumerable.Repeat(BigInteger.Zero, denominations.Count).ToList();
        updatedStock = stock?.ToList() ?? Enumerable.Repeat(BigInteger.Zero, denominations.Count).ToList();
        if (amount < 0 || denominations.Count == 0 || (stock is not null && stock.Count != denominations.Count)) return false;

        // DP keeps the optimal behaviour for non-canonical coin systems. For very large
        // amounts, retain the previous greedy-compatible path without allocating huge arrays.
        if (amount > 500_000 || amount > int.MaxValue)
            return Greedy(amount, denominations, stock, result, updatedStock);

        int target = (int)amount;
        int[] best = Enumerable.Repeat(int.MaxValue, target + 1).ToArray();
        int[] previous = Enumerable.Repeat(-1, target + 1).ToArray();
        int[] coin = Enumerable.Repeat(-1, target + 1).ToArray();
        best[0] = 0;

        for (int i = 0; i < denominations.Count; i++)
        {
            if (denominations[i] <= 0 || denominations[i] > target || denominations[i] > int.MaxValue) continue;
            int value = (int)denominations[i];
            int copies = stock is null ? target / value : (int)BigInteger.Min(stock[i], target / value);
            for (int copy = 0; copy < copies; copy++)
                for (int sum = target; sum >= value; sum--)
                    if (best[sum - value] != int.MaxValue && best[sum - value] + 1 < best[sum])
                    {
                        best[sum] = best[sum - value] + 1;
                        previous[sum] = sum - value;
                        coin[sum] = i;
                    }

            if (stock is null)
                for (int sum = value; sum <= target; sum++)
                    if (best[sum - value] != int.MaxValue && best[sum - value] + 1 < best[sum])
                    {
                        best[sum] = best[sum - value] + 1;
                        previous[sum] = sum - value;
                        coin[sum] = i;
                    }
        }

        if (best[target] == int.MaxValue || (minCoins.HasValue && best[target] < minCoins) || (maxCoins > 0 && best[target] > maxCoins)) return false;
        for (int sum = target; sum > 0; sum = previous[sum])
        {
            if (coin[sum] < 0 || previous[sum] >= sum) return false;
            result[coin[sum]]++;
        }
        if (stock is not null)
            for (int i = 0; i < result.Count; i++) updatedStock[i] = stock[i] - result[i];
        return true;
    }

    private static bool Greedy(BigInteger amount, IReadOnlyList<BigInteger> denominations, IReadOnlyList<BigInteger>? stock,
        List<BigInteger> result, List<BigInteger> updatedStock)
    {
        BigInteger remaining = amount;
        for (int i = 0; i < denominations.Count; i++)
        {
            if (denominations[i] <= 0) continue;
            BigInteger take = BigInteger.Divide(remaining, denominations[i]);
            if (stock is not null) take = BigInteger.Min(take, stock[i]);
            result[i] = take; remaining -= take * denominations[i];
            if (stock is not null) updatedStock[i] -= take;
        }
        return remaining.IsZero;
    }
}
