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

        // Las cantidades astronómicas no admiten DP proporcional al monto.
        // Se conserva la ruta exacta de division descendente del programa C para
        // monedas canonicas; nunca se fabrica un resultado si hay resto.
        const int maxDynamicAmount = 200_000;
        if (amount > maxDynamicAmount)
        {
            if (denominations.Count == 2 && denominations[0] != denominations[1])
                return SolveTwoDenominations(amount, denominations, stock, minCoins, maxCoins);

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
