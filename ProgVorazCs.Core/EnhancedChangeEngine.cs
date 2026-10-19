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
            return GreedyLarge(amount, denominations, stock, minCoins, maxCoins);

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
