using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace ProgVorazCs.Core;

/// <summary>Operaciones persistentes sobre el formato de datos del proyecto C.</summary>
public sealed class PortableServices
{
    private readonly CurrencyRepository repository;
    private readonly string root;

    public PortableServices(string root)
    {
        this.root = Path.GetFullPath(root);
        repository = new CurrencyRepository(this.root);
    }

    public CurrencyRepository Repository => repository;

    public bool Validate(string name)
    {
        CurrencyData? currency = repository.LoadCurrency(MapCurrency(name));
        return currency is not null && currency.Denominations.Count > 0
            && currency.Denominations.All(x => x > 0)
            && currency.Stock.All(x => x >= 0)
            && currency.Denominations.Zip(currency.Denominations.Skip(1), (a, b) => a > b).All(x => x);
    }

    public static string MapCurrency(string raw)
    {
        string key = TextKey.Normalize(raw).Replace(' ', '_').Replace('-', '_');
        return key switch
        {
            "eur" or "euros" => "euro",
            "usd" or "dolares" or "dollar" => "dolar",
            "jpy" or "yenes" => "yen",
            "gbp" or "libras" => "libra",
            "chf" => "franco_suizo",
            "cad" => "dolar_canadiense",
            "aud" => "dolar_australiano",
            "cny" or "rmb" => "yuan_chino",
            "mxn" => "peso_mexicano",
            _ => key
        };
    }

    public string StockAsJson()
    {
        var entries = new List<object>();
        foreach (string name in repository.LoadCurrencyNames())
        {
            if (!Validate(name)) continue;
            CurrencyData c = repository.LoadCurrency(name)!;
            entries.Add(new
            {
                nombre = c.Name,
                denominaciones = c.Denominations.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray(),
                stock = c.Stock.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()
            });
        }
        return JsonSerializer.Serialize(new { monedas = entries },
            new JsonSerializerOptions { WriteIndented = true });
    }

    public void ExportJson(string filename) =>
        File.WriteAllText(filename, StockAsJson(), new UTF8Encoding(false));

    public void ExportReport(string filename)
    {
        using StreamWriter writer = new(filename, false, new UTF8Encoding(false));
        writer.WriteLine("REPORTE GLOBAL DE INVENTARIO");
        foreach (string name in repository.LoadCurrencyNames())
        {
            if (!Validate(name)) continue;
            CurrencyData c = repository.LoadCurrency(name)!;
            writer.WriteLine(name + " | denominaciones=" + c.Denominations.Count
                + " | piezas=" + c.Stock.Aggregate(BigInteger.Zero, (a,b) => a + b)
                + " | valor_centimos=" + c.Denominations.Zip(c.Stock, (d,s) => d*s)
                    .Aggregate(BigInteger.Zero, (a,b) => a + b));
        }
    }

    public void Snapshot(string destination)
    {
        File.Copy(Path.Combine(root, "stock.txt"), destination, overwrite: true);
    }

    public void Restore(string source)
    {
        // No se restaura desde archivos malformados. No se sobrescribe el
        // original hasta completar la validacion y la copia en staging.
        string sourceFull = Path.GetFullPath(source);
        string target = Path.Combine(root, "stock.txt");
        if (!File.Exists(sourceFull)) throw new FileNotFoundException("Snapshot no encontrado.", source);
        string staging = Path.Combine(root, ".stock-restore-" + Guid.NewGuid().ToString("N"));
        File.Copy(sourceFull, staging);
        try
        {
            // Verificar por bloques con los datos originales antes de cambiar el destino.
            foreach (string name in repository.LoadCurrencyNames())
            {
                List<BigInteger>? candidate = ReadNumbers(staging, name, true);
                List<BigInteger>? denoms = repository.LoadDenominations(name);
                if (candidate is null || denoms is null || candidate.Count != denoms.Count
                    || candidate.Any(x => x < 0))
                    throw new InvalidDataException("Snapshot inconsistente para " + name);
            }
            File.Move(staging, target, true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    private static List<BigInteger>? ReadNumbers(string path, string key, bool normalizeMinusOne)
    {
        string[] tokens = File.ReadAllText(path).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int start = Array.FindIndex(tokens, x => TextKey.Normalize(x) == TextKey.Normalize(key));
        if (start < 0) return null;
        List<BigInteger> result = [];
        for (int i = start + 1; i < tokens.Length; i++)
        {
            if (normalizeMinusOne && tokens[i] == "-1") { result.Add(BigInteger.Zero); continue; }
            if (!BigInteger.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                break;
            result.Add(value);
        }
        return result;
    }

    public void RunBatch(TextReader input, TextWriter output, bool english = false, TextWriter? log = null)
    {
        output.WriteLine(english ? "mode,currency,amount,result,note" : "modo,moneda,monto,resultado,nota");
        bool first = true;
        string? line;
        while ((line = input.ReadLine()) != null)
        {
            string[] fields = line.Split(',', 4).Select(s => s.Trim()).ToArray();
            if (first && fields[0].Equals("mode", StringComparison.OrdinalIgnoreCase)
                || first && fields[0].Equals("modo", StringComparison.OrdinalIgnoreCase))
            { first = false; continue; }
            first = false;
            if (string.IsNullOrWhiteSpace(line)) continue;
            string mode = fields.ElementAtOrDefault(0) ?? "a";
            string name = MapCurrency(fields.ElementAtOrDefault(1) ?? "");
            string amountText = fields.ElementAtOrDefault(2) ?? "0";
            string rangeText = fields.ElementAtOrDefault(3) ?? "";
            if (!BigInteger.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
            {
                WriteRow(output, "", "", amountText, "ERROR", english ? "invalid amount" : "monto invalido");
                log?.WriteLine("invalid amount: " + amountText);
                continue;
            }
            CurrencyData? currency = repository.LoadCurrency(name);
            if (currency is null || !Validate(name))
            {
                WriteRow(output, "", "", amountText, "ERROR", english ? "currency not found" : "moneda no encontrada");
                log?.WriteLine("currency not found: " + name);
                continue;
            }
            int min = 0;
            int? max = null;
            if (rangeText.Length != 0)
            {
                string[] limits = rangeText.Split('-', 2);
                if (!int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    || parsed < 0 || limits.Length == 2 && (!int.TryParse(limits[1], NumberStyles.None, CultureInfo.InvariantCulture, out int _) ))
                {
                    WriteRow(output, "", name, amountText, "ERROR", "rango invalido");
                    continue;
                }
                if (limits.Length == 2)
                {
                    min = parsed;
                    max = int.Parse(limits[1], CultureInfo.InvariantCulture);
                }
                else max = parsed;
            }
            EnhancedChangeEngine.Answer? answer;
            try
            {
                answer = EnhancedChangeEngine.Compute(amount, currency.Denominations,
                    mode.Equals("b", StringComparison.OrdinalIgnoreCase) ||
                    mode.Equals("c", StringComparison.OrdinalIgnoreCase) ? currency.Stock : null, min, max);
            }
            catch (ArgumentException)
            {
                WriteRow(output, "", name, amountText, "ERROR", "rango invalido");
                continue;
            }
            catch (NotSupportedException)
            {
                WriteRow(output, "", name, amountText, "ERROR", "rango demasiado grande");
                continue;
            }
            if (answer is null)
            {
                WriteRow(output, "", name, amountText, "NO", english ? "no solution" : "sin solucion");
                continue;
            }
            string details = string.Join("", currency.Denominations.Take(8)
                .Select((denomination, i) => denomination + ":" + answer.Counts[i] + ";"));
            WriteRow(output, "", name, amountText, "OK total=" + answer.TotalCoins, details);
        }
    }

    private static void WriteRow(TextWriter output, params string[] values)
    {
        output.WriteLine(string.Join(",", values.Select(value =>
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        })));
    }
}
