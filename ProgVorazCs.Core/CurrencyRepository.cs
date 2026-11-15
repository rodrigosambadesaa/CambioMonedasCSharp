using System.Globalization;
using System.Numerics;
using System.Text;

namespace ProgVorazCs.Core;

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
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            try
            {
                lines = File.ReadAllLines(path);
                obtained = true;
                break;
            }
            catch (FileNotFoundException) when (OperatingSystem.IsWindows() &&
                attempt < 999)
            {
                Thread.Sleep(10);
            }
            catch (IOException ex) when (OperatingSystem.IsWindows() &&
                attempt < 999 && (ex.HResult & 0xFFFF) is 5 or 32 or 33)
            {
                Thread.Sleep(10);
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() &&
                attempt < 999)
            {
                // Win32 replacement can manifest as UnauthorizedAccessException
                // rather than IOException while a writer swaps stock.txt.
                // Retry the read only; never replay a completed transaction.
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
