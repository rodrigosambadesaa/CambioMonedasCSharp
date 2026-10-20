using System.Globalization;
using System.Numerics;
using System.Text;

namespace ProgVorazCs.Core;

public sealed class CurrencyData
{
    public required string Name { get; set; }
    public required List<BigInteger> Denominations { get; set; }
    public required List<BigInteger> Stock { get; set; }
}

public static class TextKey
{
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text.Trim().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new();
        foreach (char c in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

public sealed class CurrencyRepository
{
    private readonly string monedasPath;
    private readonly string stockPath;

    public CurrencyRepository(string dataRoot)
    {
        monedasPath = Path.Combine(dataRoot, "monedas.txt");
        stockPath = Path.Combine(dataRoot, "stock.txt");
    }

    public IReadOnlyList<string> LoadCurrencyNames()
    {
        List<string> tokens = ReadTokens(monedasPath);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> names = [];

        foreach (string token in tokens)
        {
            if (IsNonNegativeInteger(token))
            {
                continue;
            }

            if (seen.Add(token))
            {
                names.Add(token);
            }
        }

        return names;
    }

    public List<BigInteger>? LoadDenominations(string currencyName)
    {
        return LoadCurrencyNumbers(monedasPath, currencyName, convertMinusOneToZero: false);
    }

    public List<BigInteger>? LoadStock(string currencyName)
    {
        return LoadCurrencyNumbers(stockPath, currencyName, convertMinusOneToZero: true);
    }

    public bool UpdateStock(string currencyName, IReadOnlyList<BigInteger> stock)
    {
        int count = LoadDenominations(currencyName)?.Count ?? 0;
        return StockLedger.Replace(stockPath, currencyName, count, stock);
    }

    /// <summary>
    /// Descontar piezas de la última versión persistida sin permitir saldos negativos.
    /// Todos los procesos C# cooperantes utilizan el mismo bloqueo.
    /// </summary>
    public bool TryConsumeStock(string currencyName, IReadOnlyList<BigInteger> consumed,
        out List<BigInteger> updated)
    {
        int count = LoadDenominations(currencyName)?.Count ?? 0;
        return StockLedger.Consume(stockPath, currencyName, count, consumed, out updated);
    }

    public bool TryAdjustStock(string currencyName, int index, BigInteger delta, bool add,
        out List<BigInteger> updated)
    {
        int count = LoadDenominations(currencyName)?.Count ?? 0;
        return StockLedger.Adjust(stockPath, currencyName, count, index, delta, add, out updated);
    }

    public bool TryExchangeStock(string currencyName,
        IReadOnlyList<BigInteger> received, IReadOnlyList<BigInteger> returned,
        out List<BigInteger> updated)
    {
        int count = LoadDenominations(currencyName)?.Count ?? 0;
        return StockLedger.Exchange(stockPath, currencyName, count, received, returned,
            out updated);
    }

    public CurrencyData? LoadCurrency(string currencyName)
    {
        List<BigInteger>? denominations = LoadDenominations(currencyName);
        List<BigInteger>? stock = LoadStock(currencyName);
        if (denominations is null || stock is null || denominations.Count != stock.Count)
        {
            return null;
        }

        return new CurrencyData
        {
            Name = currencyName,
            Denominations = denominations,
            Stock = stock,
        };
    }

    private static List<string> ReadTokens(string path)
    {
        // Replacing stock.txt is atomic, but Windows may deny an unrelated
        // read-open for a few milliseconds if the replace is underway.
        // Read all lines and close the handle before returning any tokens.
        string[] lines = [];
        bool obtained = false;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                lines = File.ReadAllLines(path);
                obtained = true;
                break;
            }
            catch (FileNotFoundException) when (OperatingSystem.IsWindows() &&
                attempt < 199)
            {
                Thread.Sleep(10);
            }
            catch (IOException ex) when (OperatingSystem.IsWindows() &&
                attempt < 199 && (ex.HResult & 0xFFFF) is 5 or 32 or 33)
            {
                Thread.Sleep(10);
            }
        }
        if (!obtained) return [];
        List<string> tokens = [];
        foreach (string line in lines)
            tokens.AddRange(line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return tokens;
    }

    private static List<BigInteger>? LoadCurrencyNumbers(string path, string currencyName, bool convertMinusOneToZero)
    {
        List<string> tokens = ReadTokens(path);
        string target = TextKey.Normalize(currencyName);

        bool inSection = false;
        List<BigInteger> values = [];

        foreach (string token in tokens)
        {
            if (!inSection)
            {
                if (TextKey.Normalize(token) == target)
                {
                    inSection = true;
                }

                continue;
            }

            string t = token;
            if (convertMinusOneToZero && t == "-1")
            {
                t = "0";
            }

            if (!IsNonNegativeInteger(t))
            {
                break;
            }

            values.Add(BigInteger.Parse(t, CultureInfo.InvariantCulture));
        }

        return values.Count == 0 ? null : values;
    }

    private static bool IsNonNegativeInteger(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (char c in text)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}

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
