using System.Globalization;
using System.Numerics;
using ProgVorazCs.Core;

namespace ProgVorazCs.ConsoleApp;

/// <summary>CLI compatible con los modos no interactivos de la version original C.</summary>
public static class CliHost
{
    public static int? TryRun(string[] args, string dataRoot)
    {
        if (args.Length == 0) return null;
        bool english = args.Contains("--lang") && ValueAfter(args, "--lang") == "en"
            || Environment.GetEnvironmentVariable("PROGVORAZ_LANG") == "en";
        string? input = ValueAfter(args, "--input");
        string? output = ValueAfter(args, "--output");
        string? log = ValueAfter(args, "--log");
        string? json = ValueAfter(args, "--export-stock-json");
        string? report = ValueAfter(args, "--export-report");
        string? snap = ValueAfter(args, "--snapshot-stock");
        string? restore = ValueAfter(args, "--restore-stock");
        try
        {
            if (args.Contains("--help") || args.Contains("-h"))
            {
                System.Console.WriteLine("ProgVoraz C# --input CSV [--output CSV] [--log LOG] | --stream | --docker");
                System.Console.WriteLine("--lang es|en --json --export-stock-json FILE --export-report FILE");
                System.Console.WriteLine("--snapshot-stock FILE --restore-stock FILE --convert FROM TO CENTS [--convert-stock]");
                System.Console.WriteLine("--server [--port N] (solo 127.0.0.1) --version --help");
                return 0;
            }
            if (args.Contains("--version") || args.Contains("-v"))
            {
                System.Console.WriteLine("ProgVoraz C# (migracion .NET 10)");
                return 0;
            }
            PortableServices services = new(dataRoot);
            if (args.Contains("--server"))
            {
                string portText = ValueAfter(args, "--port") ?? "8080";
                if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture,
                    out int port) || port < 1 || port > 65535)
                    throw new ArgumentException("Puerto HTTP invalido.");
                using CancellationTokenSource cancellation = new();
                System.Console.CancelKeyPress += (_, ev) =>
                {
                    ev.Cancel = true;
                    cancellation.Cancel();
                };
                System.Console.WriteLine("Servidor local en http://127.0.0.1:" + port);
                LocalHttpServer.ServeAsync(services, port, cancellation.Token).GetAwaiter().GetResult();
                return 0;
            }
            if (json is not null) { services.ExportJson(json); return 0; }
            if (report is not null) { services.ExportReport(report); return 0; }
            if (snap is not null) { services.Snapshot(snap); return 0; }
            if (restore is not null) { services.Restore(restore); return 0; }
            if (args.Contains("--stream") || args.Contains("--docker") || input is not null)
            {
                using StreamReader? reader = input is null ? null : new StreamReader(input);
                using StreamWriter? writer = output is null ? null : new StreamWriter(output);
                using StreamWriter? logger = log is null ? null : new StreamWriter(log, true);
                services.RunBatch(reader ?? System.Console.In, writer ?? System.Console.Out, english, logger);
                writer?.Flush();
                return 0;
            }
            int conversion = Array.IndexOf(args, "--convert");
            if (conversion >= 0)
            {
                if (conversion + 3 >= args.Length) throw new ArgumentException("Faltan argumentos --convert.");
                string from = PortableServices.MapCurrency(args[conversion + 1]);
                string to = PortableServices.MapCurrency(args[conversion + 2]);
                if (!BigInteger.TryParse(args[conversion + 3], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var amount) || amount < 0)
                    throw new ArgumentException("Importe invalido.");
                decimal rate = ExchangeRates.GetRateAsync(from, to, dataRoot).GetAwaiter().GetResult();
                BigInteger destinationAmount = ExchangeRates.ConvertCents(amount, rate);
                CurrencyData currency = services.Repository.LoadCurrency(to)
                    ?? throw new ArgumentException("Moneda desconocida: " + to);
                var answer = EnhancedChangeEngine.Compute(destinationAmount, currency.Denominations,
                    args.Contains("--convert-stock") ? currency.Stock : null);
                if (answer is null) throw new InvalidOperationException("No hay descomposicion exacta.");
                System.Console.WriteLine("Origen=" + amount + " Destino=" + destinationAmount + " Tasa=" + rate.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < answer.Counts.Length; i++)
                    System.Console.WriteLine(currency.Denominations[i] + ":" + answer.Counts[i]);
                return 0;
            }
            throw new ArgumentException("Opcion no reconocida. Use --help.");
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidDataException
            or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or System.Text.Json.JsonException or HttpRequestException)
        {
            System.Console.Error.WriteLine(error.Message);
            return 2;
        }
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
