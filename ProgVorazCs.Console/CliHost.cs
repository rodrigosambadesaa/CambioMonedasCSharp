using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using ProgVorazCs.Core;

namespace ProgVorazCs.ConsoleApp;

/// <summary>CLI compatible con los modos no interactivos de la version original C.</summary>
public static class CliHost
{
    public static int? TryRun(string[] args, string dataRoot)
    {
        if (args.Length == 0) return null;
        // In C, --json enables a result renderer and then enters the TUI.
        if (args.Contains("--json"))
        {
            Environment.SetEnvironmentVariable("PROGVORAZ_JSON", "1");
            if (args.Length == 1) return null;
        }
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
                System.Console.WriteLine("--lang es|en --json (modo interactivo) --export-stock-json FILE --export-report FILE");
                System.Console.WriteLine("--snapshot-stock FILE --restore-stock FILE --convert FROM TO CENTS [--convert-stock]");
                System.Console.WriteLine("--specific CURRENCY RECEIVED_CSV RETURNED_CSV [--specific-stock]");
                System.Console.WriteLine("--history [--tail N] --summary CURRENCY");
                System.Console.WriteLine("--gui (Windows) --server [--port N] (solo 127.0.0.1) --version --help");
                return 0;
            }
            if (args.Contains("--version") || args.Contains("-v"))
            {
                System.Console.WriteLine("ProgVoraz C# (migracion .NET 10)");
                return 0;
            }
            if (args.Contains("--gui") || args.Contains("gui")) return LaunchGui(dataRoot);
            PortableServices services = new(dataRoot);
            if (args.Contains("--history"))
            {
                int limit = 100;
                string? rawLimit = ValueAfter(args, "--tail");
                if (rawLimit is not null &&
                    (!int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture,
                        out limit) || limit < 1 || limit > 1000))
                    throw new ArgumentException("Limite de historial fuera de 1-1000.");
                foreach (string entry in services.History(limit)) System.Console.WriteLine(entry);
                return 0;
            }
            if (args.Contains("--summary"))
            {
                string? key = ValueAfter(args, "--summary");
                if (key is null) throw new ArgumentException("Indique moneda para --summary");
                System.Console.WriteLine(services.CurrencySummary(key));
                return 0;
            }
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
            int specificIndex = Array.IndexOf(args, "--specific");
            if (specificIndex >= 0)
            {
                if (specificIndex + 3 >= args.Length)
                    throw new ArgumentException("Se requiere moneda y dos listas de cantidades.");
                string key = PortableServices.MapCurrency(args[specificIndex + 1]);
                CurrencyData currency = services.Repository.LoadCurrency(key)
                    ?? throw new ArgumentException("Moneda desconocida: " + key);
                BigInteger[] received = ParseCounts(args[specificIndex + 2],
                    currency.Denominations.Count);
                BigInteger[] returned = ParseCounts(args[specificIndex + 3],
                    currency.Denominations.Count);
                var result = SpecificExchange.Execute(services.Repository, key,
                    received, returned, args.Contains("--specific-stock"));
                if (result is null)
                    throw new InvalidOperationException("Importes distintos, stock insuficiente o error de persistencia.");
                System.Console.WriteLine("OK moneda=" + key + " total_centimos=" + result.TotalCents);
                for (int i = 0; i < currency.Denominations.Count; i++)
                    System.Console.WriteLine(currency.Denominations[i] +
                        " recibido=" + received[i] + " entregado=" + returned[i]
                        + " stock=" + result.CurrentStock[i]);
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
            or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or System.Text.Json.JsonException or HttpRequestException or Win32Exception)
        {
            System.Console.Error.WriteLine(error.Message);
            return 2;
        }
    }

    private static int LaunchGui(string dataRoot)
    {
        if (!OperatingSystem.IsWindows())
            throw new NotSupportedException("La GUI WinForms sólo se ejecuta en Windows.");

        string project = Path.Combine(dataRoot, "ProgVorazCs.Gui");
        // An installed release places the GUI beside the console executable.
        // Development builds place it under the WinForms project.
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "ProgVorazCs.Gui.exe"),
            Path.Combine(project, "bin", "Release", "net10.0-windows", "ProgVorazCs.Gui.exe"),
            Path.Combine(project, "bin", "Debug", "net10.0-windows", "ProgVorazCs.Gui.exe")
        ];
        string? executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
            throw new FileNotFoundException("Compile la GUI WinForms antes de usar --gui.");

        using Process? launched = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = dataRoot,
            UseShellExecute = true
        });
        if (launched is null)
            throw new InvalidOperationException("No se pudo iniciar la GUI.");
        System.Console.WriteLine("Interfaz WinForms iniciada.");
        return 0;
    }

    private static BigInteger[] ParseCounts(string input, int required)
    {
        string[] tokens = input.Split(',');
        if (tokens.Length != required)
            throw new ArgumentException("Numero incorrecto de cantidades. Se requieren " + required);
        var result = new BigInteger[required];
        for (int i = 0; i < tokens.Length; i++)
            if (!BigInteger.TryParse(tokens[i], NumberStyles.None,
                    CultureInfo.InvariantCulture, out result[i]) || result[i] < 0)
                throw new ArgumentException("Cantidad de moneda no valida en indice " + i);
        return result;
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
