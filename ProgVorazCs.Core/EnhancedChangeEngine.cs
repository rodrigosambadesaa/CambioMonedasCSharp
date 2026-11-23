using System.Numerics;

namespace ProgVorazCs.Core;

/// <summary>
/// Motor de descomposicion: BigInteger, minimo numero de piezas, inventario opcional,
/// limites de piezas y busqueda de importe cercano. No depende del ejecutable C.
/// </summary>
public static class EnhancedChangeEngine
{
    public sealed record Answer(BigInteger Covered, BigInteger[] Counts)
    {
        public BigInteger TotalCoins => Counts.Aggregate(BigInteger.Zero, (a, b) => a + b);
    }

    private sealed record Node(Node? Previous, int Denomination, int Quantity);

    public static Answer? Compute(BigInteger amount, IReadOnlyList<BigInteger> denominations,
        IReadOnlyList<BigInteger>? stock = null, int minCoins = 0,
        int? maxCoins = null, bool nearest = false)
    {
        ArgumentNullException.ThrowIfNull(denominations);
        if (amount < 0 || denominations.Count == 0 || denominations.Any(d => d <= 0)
            || stock is not null && (stock.Count != denominations.Count || stock.Any(x => x < 0))
            || minCoins < 0 || maxCoins < 0 || maxCoins.HasValue && minCoins > maxCoins.Value)
            throw new ArgumentException("Importe, denominaciones, stock o rango invalidos.");

        if (amount.IsZero)
            return minCoins == 0 && !nearest
                ? new Answer(BigInteger.Zero, new BigInteger[denominations.Count]) : null;

        // One coin is also solvable exactly across any number of
        // denominations. On large amounts, the fallback to exact change
        // incorrectly rejects a valid closest smaller denomination.
        if (nearest && maxCoins == 1)
        {
            int bestIndex = -1;
            for (int i = 0; i < denominations.Count; ++i)
            {
                if (denominations[i] > amount ||
                    stock is not null && stock[i].IsZero)
                    continue;
                if (bestIndex < 0 || denominations[i] > denominations[bestIndex])
                    bestIndex = i;
            }
            if (bestIndex < 0) return null;
            BigInteger[] counts = new BigInteger[denominations.Count];
            counts[bestIndex] = BigInteger.One;
            return new Answer(denominations[bestIndex], counts);
        }

        // With at most two coins, exhaustive O(n^2) enumeration is exact even
        // for astronomical amounts. The large-amount greedy fallback cannot
        // answer nearest-change queries (for example 800000+400000 <= 1200001).
        if (nearest && maxCoins == 2 && denominations.Count <= 256)
        {
            BigInteger best = BigInteger.Zero;
            int bestFirst = -1, bestSecond = -1;
            if (minCoins <= 1)
                for (int i = 0; i < denominations.Count; i++)
                    if (denominations[i] <= amount &&
                        (stock is null || stock[i] >= 1) &&
                        denominations[i] > best)
                    {
                        best = denominations[i];
                        bestFirst = i;
                        bestSecond = -1;
                    }

            for (int i = 0; i < denominations.Count; i++)
                for (int j = i; j < denominations.Count; j++)
                {
                    if (stock is not null &&
                        (stock[i] < (i == j ? 2 : 1) || stock[j] < 1))
                        continue;
                    BigInteger sum = denominations[i] + denominations[j];
                    if (sum <= amount && sum > best)
                    {
                        best = sum;
                        bestFirst = i;
                        bestSecond = j;
                    }
                }

            if (bestFirst < 0) return null;
            BigInteger[] nearestCounts = new BigInteger[denominations.Count];
            nearestCounts[bestFirst]++;
            if (bestSecond >= 0) nearestCounts[bestSecond]++;
            return new Answer(best, nearestCounts);
        }

        // A single denomination has a closed-form optimum even for amounts
        // larger than Int32/UInt64. The closest-change path must enforce both
        // stock and coin-count bounds instead of falling back to exact change.
        if (denominations.Count == 1)
        {
            BigInteger count = amount / denominations[0];
            if (stock is not null)
                count = BigInteger.Min(count, stock[0]);
            if (maxCoins.HasValue)
                count = BigInteger.Min(count, maxCoins.Value);
            BigInteger singleCovered = count * denominations[0];
            if (count < minCoins || nearest && singleCovered == 0 ||
                !nearest && singleCovered != amount)
                return null;
            return new Answer(singleCovered, [count]);
        }

        // Reduce a common monetary unit before selecting the large-amount path.
        // Stock and coin counts remain unchanged, including for nearest change:
        // only complete multiples of the unit are representable.
        BigInteger unit = denominations.Aggregate(BigInteger.Zero,
            (current, value) => BigInteger.GreatestCommonDivisor(current, value));
        if (unit > BigInteger.One)
        {
            if (!nearest && amount % unit != 0)
                return null;
            Answer? normalized = Compute(amount / unit,
                denominations.Select(value => value / unit).ToArray(),
                stock, minCoins, maxCoins, nearest);
            return normalized is null ? null
                : new Answer(normalized.Covered * unit, normalized.Counts);
        }

        // The ordinary amount-only DP retains just the shortest answer;
        // a positive lower bound can require a longer valid composition.
        // Use the exact Diophantine solver for two distinct denominations.
        if (!nearest && denominations.Count == 2 &&
            denominations[0] != denominations[1] && minCoins > 0)
            return SolveTwoDenominations(amount, denominations, stock,
                                         minCoins, maxCoins);

        // Las cantidades astronómicas no admiten DP proporcional al monto.
        // Se conserva la ruta exacta de division descendente del programa C para
        // monedas canonicas; nunca se fabrica un resultado si hay resto.
        const int maxDynamicAmount = 200_000;
        if (amount > maxDynamicAmount)
        {
            // Certify any exact solution of at most four pieces before costly DP.
            // Lack of such a witness is inconclusive, never an impossibility proof.
            if (!nearest)
            {
                Answer? small = ExactUpToFourCoins(
                    amount, denominations, stock, minCoins, maxCoins);
                if (small is not null) return small;
                Answer? longer = ExactFiveToTwentyCoins(
                    amount, denominations, stock, minCoins, maxCoins);
                if (longer is not null) return longer;
            }

            // Exact sparse knapsack for finite inventories, independent
            // of the numeric size of amount or coin denominations.
            if (stock is not null && TrySparseLimitedLarge(amount, denominations,
                stock, minCoins, maxCoins, nearest, out Answer? sparse))
                return sparse;

            if (denominations.Count == 2 && denominations[0] != denominations[1])
            {
                if (nearest && minCoins == 0 && maxCoins is null)
                {
                    Answer? closest = stock is null
                        ? NearestTwoDenominationsUnlimited(amount, denominations)
                        : NearestTwoDenominationsWithStock(amount, denominations, stock);
                    if (closest is not null)
                        return closest;
                }
                return SolveTwoDenominations(amount, denominations, stock, minCoins, maxCoins);
            }


            // Shortest paths over residues are EXACT for unlimited stock once the
            // minimal-penalty representative fits within the requested amount.
            // This matters for non-canonical systems where greedy is suboptimal.
            if (stock is null)
            {
                Answer? certified = ResidueOptimalLarge(amount, denominations);
                if (certified is not null)
                {
                    if (certified.TotalCoins >= minCoins &&
                        (!maxCoins.HasValue || certified.TotalCoins <= maxCoins))
                        return certified;
                    if (certified.TotalCoins > (maxCoins ?? int.MaxValue))
                        return null; // Even the global optimum exceeds the maximum.
                    if (minCoins > certified.TotalCoins)
                        throw new NotSupportedException(
                            "El mínimo de piezas requiere una búsqueda adicional de soluciones subóptimas.");
                }
            }
            return GreedyLarge(amount, denominations, stock, minCoins, maxCoins);
        }

        int target = (int)amount;
        int[] values = new int[denominations.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = denominations[i] > target ? target + 1 : (int)denominations[i];

        if (minCoins == 0 && maxCoins is null)
        {
            // Una única tabla DP cubre todos los importes hasta target.
            // La implementación anterior reconstruía la DP por importe.
            return DynamicOptimal(target, values, stock, nearest);
        }

        // Fast path: when the global optimum already falls within the required
        // interval, no (amount, coin-count) state-space search is needed.
        // Critical for large amounts and generous ranges such as 1-100 pieces.
        Answer? optimum = DynamicOptimal(target, values, stock, false);
        if (optimum is not null && optimum.TotalCoins >= minCoins
            && (maxCoins is null || optimum.TotalCoins <= maxCoins.Value))
            return optimum;

        int ceiling = Math.Min(maxCoins ?? target, target);
        if (ceiling < minCoins) return null;
        if (target > 20_000 || ceiling > 300)
            throw new NotSupportedException("Rango exacto demasiado grande para busqueda acotada.");

        // Estado (suma, cantidad), con nodo inmutable para evitar el bug de
        // predecesores sobrescritos al optimizar inventarios limitados.
        var states = new Dictionary<(int Sum, int Coins), Node?>
        {
            [(0, 0)] = null
        };
        for (int i = 0; i < values.Length; ++i)
        {
            int value = values[i];
            if (value > target) continue;
            int maxQuantity = Math.Min(target / value, ceiling);
            if (stock is not null)
                maxQuantity = (int)BigInteger.Min(stock[i], maxQuantity);
            var next = new Dictionary<(int Sum, int Coins), Node?>(states);
            foreach (var state in states)
            {
                for (int count = 1; count <= maxQuantity; count++)
                {
                    int sum = state.Key.Sum + count * value;
                    int coins = state.Key.Coins + count;
                    if (sum > target || coins > ceiling) break;
                    var key = (sum, coins);
                    next.TryAdd(key, new Node(state.Value, i, count));
                }
            }
            states = next;
            if (states.Count > 1_000_000)
                throw new NotSupportedException("Demasiados estados para el rango solicitado.");
        }

        int covered = target;
        while (covered > 0)
        {
            for (int count = minCoins; count <= ceiling; ++count)
                if (states.TryGetValue((covered, count), out Node? plan))
                    return new Answer(covered, Extract(plan, denominations.Count));
            if (!nearest) break;
            covered--;
        }
        return null;
    }

    private static Answer? DynamicOptimal(int target, int[] values, IReadOnlyList<BigInteger>? stock, bool nearest)
    {
        const int inf = int.MaxValue / 4;
        int[] minimum = Enumerable.Repeat(inf, target + 1).ToArray();
        Node?[] plans = new Node?[target + 1];
        minimum[0] = 0;
        for (int i = 0; i < values.Length; i++)
        {
            int v = values[i];
            if (v > target) continue;
            if (stock is null)
            {
                for (int s = v; s <= target; s++)
                    if (minimum[s - v] != inf && minimum[s - v] + 1 < minimum[s])
                    {
                        minimum[s] = minimum[s - v] + 1;
                        plans[s] = new Node(plans[s - v], i, 1);
                    }
            }
            else
            {
                int units = (int)BigInteger.Min(stock[i], target / v);
                // Descomposicion binaria acotada: O(log(stock)*amount).
                for (int chunk = 1; units > 0; chunk = chunk > units / 2 ? units : chunk * 2)
                {
                    int take = Math.Min(units, chunk);
                    units -= take;
                    int cost = take * v;
                    for (int s = target; s >= cost; --s)
                        if (minimum[s - cost] != inf && minimum[s - cost] + take < minimum[s])
                        {
                            minimum[s] = minimum[s - cost] + take;
                            plans[s] = new Node(plans[s - cost], i, take);
                        }
                }
            }
        }
        int covered = target;
        if (nearest)
            while (covered > 0 && minimum[covered] == inf) --covered;
        if (minimum[covered] == inf || nearest && covered == 0) return null;
        return new Answer(covered, Extract(plans[covered], values.Length));
    }

    /// <summary>
    /// Exact unlimited-change solver for very large amounts when the maximum
    /// denomination is at most 50,000 cents. For each residue modulo the
    /// largest coin, Dijkstra minimizes (largest-denomination * coin count - sum).
    /// Filling the residual amount with largest coins then minimizes coin count.
    /// The representative's sum must fit within the requested amount; otherwise
    /// the result is not certified and we let the caller use its fallback.
    /// </summary>
    private static Answer? ResidueOptimalLarge(BigInteger amount,
        IReadOnlyList<BigInteger> denominations)
    {
        BigInteger max = denominations.Max();
        if (max <= 1 || max > 50_000) return null;
        int modulus = (int)max;
        var penalty = Enumerable.Repeat(long.MaxValue, modulus).ToArray();
        var smallestBase = Enumerable.Repeat(long.MaxValue, modulus).ToArray();
        var predecessor = Enumerable.Repeat(-1, modulus).ToArray();
        var chosenCoin = Enumerable.Repeat(-1, modulus).ToArray();
        var queue = new PriorityQueue<int, (long Penalty, long Amount)>();
        penalty[0] = smallestBase[0] = 0;
        queue.Enqueue(0, (0, 0));
        while (queue.TryDequeue(out int residue, out var cost))
        {
            if (cost.Penalty != penalty[residue] || cost.Amount != smallestBase[residue])
                continue;
            for (int i = 0; i < denominations.Count; i++)
            {
                BigInteger denomination = denominations[i];
                if (denomination >= max) continue; // max is filled at the end.
                int coin = (int)denomination;
                int next = (residue + coin) % modulus;
                long nextPenalty = cost.Penalty + modulus - coin;
                long nextAmount = cost.Amount + coin;
                if (nextPenalty > penalty[next] ||
                    nextPenalty == penalty[next] && nextAmount >= smallestBase[next])
                    continue;
                penalty[next] = nextPenalty;
                smallestBase[next] = nextAmount;
                predecessor[next] = residue;
                chosenCoin[next] = i;
                queue.Enqueue(next, (nextPenalty, nextAmount));
            }
        }
        int targetResidue = (int)(amount % modulus);
        if (penalty[targetResidue] == long.MaxValue ||
            smallestBase[targetResidue] > amount)
            return null;
        BigInteger[] counts = new BigInteger[denominations.Count];
        int firstLargest = Enumerable.Range(0, denominations.Count)
            .First(i => denominations[i] == max);
        counts[firstLargest] = (amount - smallestBase[targetResidue]) / max;
        int current = targetResidue;
        for (int steps = 0; current != 0 && steps < modulus; steps++)
        {
            int i = chosenCoin[current];
            if (i < 0) return null;
            counts[i]++;
            current = predecessor[current];
        }
        if (current != 0) return null;
        return new Answer(amount, counts);
    }

    /// <summary>
    /// Exact solution of a*x + b*y = amount, with optional inventories.
    /// Extended Euclid gives one residue class for x; we choose the largest
    /// feasible x because the higher denomination minimizes the total number
    /// of pieces. Constraints on minimum/maximum pieces are applied using
    /// closed-form moves across the Diophantine solution lattice.
    /// </summary>
    /// <summary>
    /// Exact bounded subset-sum with binary-split inventory items. States
    /// retain both value and number of pieces, preserving non-minimal solutions
    /// needed for an explicit lower coin bound. Only attempted for at most 512
    /// physical coins. State explosion fails explicitly rather than silently
    /// treating a heuristic as mathematically optimal.
    /// </summary>
    private static bool TrySparseLimitedLarge(BigInteger amount,
        IReadOnlyList<BigInteger> denominations, IReadOnlyList<BigInteger> stock,
        int minCoins, int? maxCoins, bool nearest, out Answer? answer)
    {
        answer = null;
        BigInteger totalStock = stock.Aggregate(BigInteger.Zero, (a,b) => a+b);
        if (totalStock > 512) return false;
        var states = new Dictionary<(BigInteger Sum, int Coins), Node?>
        {
            [(BigInteger.Zero, 0)] = null
        };
        for (int i = 0; i < denominations.Count; i++)
        {
            int remaining = (int)stock[i];
            for (int chunk = 1; remaining > 0;)
            {
                int taken = Math.Min(chunk, remaining);
                remaining -= taken;
                BigInteger value = denominations[i] * taken;
                var next = new Dictionary<(BigInteger Sum, int Coins), Node?>(states);
                foreach (var (key, previous) in states)
                {
                    BigInteger candidateAmount = key.Sum + value;
                    int candidateCoins = key.Coins + taken;
                    if (candidateAmount > amount ||
                        maxCoins.HasValue && candidateCoins > maxCoins.Value)
                        continue;
                    next.TryAdd((candidateAmount, candidateCoins),
                        new Node(previous, i, taken));
                }
                if (next.Count > 250_000)
                    throw new NotSupportedException(
                        "La búsqueda exacta superó 250.000 estados de inventario.");
                states = next;
                chunk = chunk > remaining / 2 ? remaining : chunk * 2;
            }
        }
        BigInteger bestCovered = -1;
        int bestCoins = int.MaxValue;
        Node? bestPlan = null;
        foreach (var (key, plan) in states)
        {
            if (key.Coins < minCoins || maxCoins.HasValue && key.Coins > maxCoins.Value
                || nearest && key.Sum == 0
                || !nearest && key.Sum != amount)
                continue;
            if (key.Sum > bestCovered || key.Sum == bestCovered && key.Coins < bestCoins)
            {
                bestCovered = key.Sum;
                bestCoins = key.Coins;
                bestPlan = plan;
            }
        }
        if (bestCovered >= 0)
            answer = new Answer(bestCovered, Extract(bestPlan, denominations.Count));
        return true;
    }

    /// <summary>
    /// Exact nearest amount for two unlimited positive denominations <= 50,000
    /// with no coin-count constraints. The largest denomination alone gives
    /// the reachable lower bound floor(amount / largest) * largest. Every
    /// reachable value is a multiple of gcd, so at most largest/gcd exact
    /// Diophantine candidates need consideration, independently of BigInteger size.
    /// Other cases retain their existing solver or explicit limitations.
    /// </summary>
    private static Answer? NearestTwoDenominationsUnlimited(
        BigInteger amount, IReadOnlyList<BigInteger> denominations)
    {
        BigInteger largest = BigInteger.Max(denominations[0], denominations[1]);
        if (largest > 50_000) return null;
        BigInteger gcd = BigInteger.GreatestCommonDivisor(
            denominations[0], denominations[1]);
        BigInteger candidate = amount - amount % gcd;
        BigInteger guaranteed = amount / largest * largest;

        while (candidate >= guaranteed && candidate > 0)
        {
            Answer? exact = SolveTwoDenominations(
                candidate, denominations, null, 0, null);
            if (exact is not null) return exact;
            candidate -= gcd;
        }
        return null;
    }

    /// <summary>
    /// Exact closest change with two denominations and finite arbitrary-size
    /// stock, without explicit coin-count bounds. For the smaller coin b,
    /// quantities in the same class modulo a/gcd(a,b) have the same remainder.
    /// Within each class, the amount grows while the larger-coin stock is
    /// saturated, then stays constant after the cap is lifted. Thus only the
    /// transition's two neighboring quantities can be optimal.
    /// Complexity O(larger denomination/gcd), irrespective of the stock size.
    /// </summary>
    private static Answer? NearestTwoDenominationsWithStock(
        BigInteger amount, IReadOnlyList<BigInteger> denominations,
        IReadOnlyList<BigInteger> stock)
    {
        int high = denominations[0] > denominations[1] ? 0 : 1;
        int low = 1 - high;
        BigInteger a = denominations[high], b = denominations[low];
        if (a > 50_000) return null;
        int period = (int)(a / BigInteger.GreatestCommonDivisor(a, b));
        BigInteger maximumY = BigInteger.Min(stock[low], amount / b);
        BigInteger saturatedThrough = amount >= a * stock[high]
            ? (amount - a * stock[high]) / b
            : -BigInteger.One;
        Answer? best = null;

        void Consider(BigInteger y)
        {
            if (y < 0 || y > maximumY) return;
            BigInteger x = BigInteger.Min(stock[high], (amount - b * y) / a);
            BigInteger covered = a * x + b * y;
            if (covered <= 0) return;
            BigInteger coins = x + y;
            if (best is not null &&
                (covered < best.Covered ||
                 covered == best.Covered && coins >= best.TotalCoins))
                return;
            BigInteger[] counts = new BigInteger[2];
            counts[high] = x;
            counts[low] = y;
            best = new Answer(covered, counts);
        }

        for (int residue = 0; residue < period && residue <= maximumY; residue++)
        {
            BigInteger first = residue;
            BigInteger saturatedLimit = BigInteger.Min(maximumY, saturatedThrough);
            if (saturatedLimit >= first)
                Consider(first + (saturatedLimit - first) / period * period);

            BigInteger uncapped = saturatedThrough < first
                ? first
                : first + ((saturatedThrough - first) / period + 1) * period;
            if (uncapped <= maximumY)
                Consider(uncapped);
        }
        return best;
    }

    /// <summary>
    /// Certifies the minimum count if a change exists with at most four pieces.
    /// Two-piece sums make three/four-piece searches quadratic, not quartic.
    /// A null result is inconclusive (never proof that change is impossible).
    /// No amount-sized allocations, even for enormous BigInteger amounts.
    /// </summary>
    private static Answer? ExactUpToFourCoins(BigInteger amount,
        IReadOnlyList<BigInteger> denominations, IReadOnlyList<BigInteger>? stock,
        int minCoins, int? maxCoins)
    {
        int n = denominations.Count;
        if (n == 0 || n > 64 || minCoins > 4 || maxCoins == 0)
            return null;
        int limit = Math.Min(4, maxCoins ?? 4);

        bool Available(params int[] indices)
        {
            if (stock is null) return true;
            for (int i = 0; i < indices.Length; i++)
            {
                int used = 1;
                for (int j = i + 1; j < indices.Length; j++)
                    if (indices[j] == indices[i]) used++;
                if (stock[indices[i]] < used) return false;
            }
            return true;
        }

        Answer Result(params int[] indices)
        {
            var counts = new BigInteger[n];
            foreach (int i in indices) counts[i]++;
            return new Answer(amount, counts);
        }

        if (minCoins <= 1 && limit >= 1)
            for (int i = 0; i < n; i++)
                if (denominations[i] == amount && Available(i))
                    return Result(i);

        var pairs = new Dictionary<BigInteger, List<(int I, int J)>>();
        if (limit < 2) return null;
        for (int i = 0; i < n; i++)
            for (int j = i; j < n; j++)
            {
                if (!Available(i, j)) continue;
                BigInteger sum = denominations[i] + denominations[j];
                if (sum > amount) continue;
                if (minCoins <= 2 && sum == amount) return Result(i, j);
                if (!pairs.TryGetValue(sum, out var entries))
                    pairs[sum] = entries = [];
                entries.Add((i, j));
            }

        if (limit >= 3 && minCoins <= 3)
            for (int i = 0; i < n; i++)
            {
                BigInteger complement = amount - denominations[i];
                if (!pairs.TryGetValue(complement, out var entries)) continue;
                foreach (var (a, b) in entries)
                    if (Available(i, a, b))
                        return Result(i, a, b);
            }

        if (limit >= 4 && minCoins <= 4)
            foreach (var (sum, entries) in pairs)
            {
                if (!pairs.TryGetValue(amount - sum, out var opposite)) continue;
                foreach (var (a, b) in entries)
                    foreach (var (c, d) in opposite)
                        if (Available(a, b, c, d))
                            return Result(a, b, c, d);
            }
        return null;
    }

    /// <summary>
    /// Exhaustive, amount-independent search for an admissible solution of
    /// 5–8 coins for at most eight denominations, 9–12 for at most five,
    /// and 13–20 for at most four. The search space is bounded.
    /// A witness is optimal because 1-4 pieces were already exhausted and
    /// the remaining depths are examined in ascending order. Failure to find
    /// one is inconclusive, not proof of impossibility.
    /// </summary>
    private static Answer? ExactFiveToTwentyCoins(BigInteger amount,
        IReadOnlyList<BigInteger> denominations, IReadOnlyList<BigInteger>? stock,
        int minCoins, int? maxCoins)
    {
        int n = denominations.Count;
        if (n == 0 || n > 8 || minCoins > (n <= 4 ? 20 : n <= 5 ? 12 : 8) || maxCoins is < 5)
            return null;
        int maxDepth = Math.Min(n <= 4 ? 20 : n <= 5 ? 12 : 8, maxCoins ?? 20);
        // Mathematical upper bound on any payable sum with this many pieces.
        // Prevent costly recursion for astronomically large BigInteger amounts.
        if (amount > denominations.Max() * maxDepth)
            return null;
        var counts = new BigInteger[n];

        bool Visit(BigInteger remaining, int first, int left)
        {
            if (left == 0) return remaining.IsZero;
            if (remaining.IsZero) return false;
            for (int i = first; i < n; i++)
            {
                if (denominations[i] > remaining ||
                    stock is not null && counts[i] >= stock[i])
                    continue;
                counts[i]++;
                if (Visit(remaining - denominations[i], i, left - 1))
                    return true;
                counts[i]--;
            }
            return false;
        }

        for (int depth = Math.Max(5, minCoins);
             depth <= Math.Min(n <= 4 ? 20 : n <= 5 ? 12 : 8, maxCoins ?? 20); depth++)
            if (Visit(amount, 0, depth))
                return new Answer(amount, (BigInteger[])counts.Clone());
        return null;
    }

    private static Answer? SolveTwoDenominations(BigInteger amount,
        IReadOnlyList<BigInteger> denominations, IReadOnlyList<BigInteger>? stock,
        int minCoins, int? maxCoins)
    {
        int high = denominations[0] > denominations[1] ? 0 : 1;
        int low = 1 - high;
        BigInteger a = denominations[high], b = denominations[low];
        BigInteger gcd = BigInteger.GreatestCommonDivisor(a, b);
        if (amount % gcd != 0) return null;
        BigInteger aReduced = a / gcd, bReduced = b / gcd;
        BigInteger x0 = bReduced == 1 ? BigInteger.Zero :
            ((amount / gcd) * ModularInverse(aReduced, bReduced)) % bReduced;
        BigInteger upper = amount / a;
        BigInteger lower = BigInteger.Zero;
        if (stock is not null)
        {
            upper = BigInteger.Min(upper, stock[high]);
            BigInteger lowerNumerator = amount - b * stock[low];
            if (lowerNumerator > 0)
                lower = (lowerNumerator + a - 1) / a;
        }
        if (upper < x0) return null;
        BigInteger x = x0 + ((upper - x0) / bReduced) * bReduced;
        if (x < lower) return null;
        BigInteger y = (amount - a * x) / b;
        BigInteger total = x + y;
        // Moving x downward by b/g increases y by a/g. Thus each step
        // adds exactly a/g - b/g coins while preserving the amount.
        if (total < minCoins)
        {
            BigInteger increment = aReduced - bReduced;
            BigInteger moves = (minCoins - total + increment - 1) / increment;
            x -= moves * bReduced;
            if (x < lower) return null;
            y += moves * aReduced;
            total = x + y;
        }
        if (maxCoins.HasValue && total > maxCoins.Value) return null;
        BigInteger[] counts = new BigInteger[2];
        counts[high] = x;
        counts[low] = y;
        return new Answer(amount, counts);
    }

    private static BigInteger ModularInverse(BigInteger value, BigInteger modulus)
    {
        BigInteger oldR = modulus, r = value % modulus;
        BigInteger oldT = BigInteger.Zero, t = BigInteger.One;
        while (r != 0)
        {
            BigInteger q = oldR / r;
            (oldR, r) = (r, oldR - q * r);
            (oldT, t) = (t, oldT - q * t);
        }
        if (oldR != 1)
            throw new ArgumentException("Las denominaciones reducidas no son coprimas.");
        return (oldT % modulus + modulus) % modulus;
    }

    private static Answer? GreedyLarge(BigInteger amount, IReadOnlyList<BigInteger> denominations,
        IReadOnlyList<BigInteger>? stock, int minCoins, int? maxCoins)
    {
        BigInteger remaining = amount;
        BigInteger[] counts = new BigInteger[denominations.Count];
        foreach (int i in Enumerable.Range(0, denominations.Count).OrderByDescending(i => denominations[i]))
        {
            counts[i] = remaining / denominations[i];
            if (stock is not null) counts[i] = BigInteger.Min(counts[i], stock[i]);
            remaining -= counts[i] * denominations[i];
        }
        BigInteger total = counts.Aggregate(BigInteger.Zero, (a, b) => a + b);
        return remaining == 0 && total >= minCoins && (maxCoins is null || total <= maxCoins.Value)
            ? new Answer(amount, counts) : null;
    }

    private static BigInteger[] Extract(Node? plan, int size)
    {
        BigInteger[] result = new BigInteger[size];
        while (plan is not null)
        {
            result[plan.Denomination] += plan.Quantity;
            plan = plan.Previous;
        }
        return result;
    }
}
