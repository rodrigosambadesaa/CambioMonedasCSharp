using System.Globalization;
using System.Numerics;
using System.Text.Json;
using ProgVorazCs.Core;

internal static class AppConsole
{
    public static int Run(string[] args)
    {
        string? dataRoot = FindDataRoot();
        if (dataRoot is null)
        {
            Console.WriteLine("No se encontraron monedas.txt y stock.txt.");
            return 1;
        }

        int? commandResult = ProgVorazCs.ConsoleApp.CliHost.TryRun(args, dataRoot);
        if (commandResult.HasValue) return commandResult.Value;

        CurrencyRepository repo = new(dataRoot);
        PortableServices services = new(dataRoot);
        while (true)
        {
            int mode = AskMode(services);
            if (mode == 0)
            {
                break;
            }

            if (!RunMode(repo, mode))
            {
                break;
            }
        }

        Console.WriteLine("Gracias por utilizar este programa.");
        return 0;
    }

    private static bool RunMode(CurrencyRepository repo, int mode)
    {
        while (true)
        {
            ShowAvailableCurrencies(repo);
            Console.Write("Nombre de la moneda (ej: euro, dolar, yen), 'modo', 'volver' o 'salir': ");
            string input = (Console.ReadLine() ?? string.Empty).Trim();
            string command = TextKey.Normalize(input);

            if (command == "salir")
            {
                return false;
            }

            if (command is "modo" or "volver")
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            CurrencyData? currency = repo.LoadCurrency(PortableServices.MapCurrency(command));
            if (currency is null)
            {
                Console.WriteLine("No se pudo cargar moneda/stock para esa clave.");
                continue;
            }

            if (mode == 'c')
            {
                if (!RunAdmin(repo, currency, PortableServices.MapCurrency(command)))
                {
                    return false;
                }

                continue;
            }

            if (!RunChange(repo, currency, PortableServices.MapCurrency(command), mode == 'b'))
            {
                return false;
            }
        }
    }

    // The C reference lists all available currencies when entering a mode.
    // Read the actual repository file; no hard-coded list of nine keys.
    private static void ShowAvailableCurrencies(CurrencyRepository repository)
    {
        Console.WriteLine("--- Divisas disponibles ---");
        foreach (string name in repository.LoadCurrencyNames())
        {
            CurrencyData? currency = repository.LoadCurrency(name);
            if (currency is null || currency.Denominations.Count == 0)
                continue;
            Console.WriteLine($"  {currency.Name}: " +
                $"{currency.Denominations.Count} denominaciones");
        }
        Console.WriteLine("---------------------------");
    }

    private static bool RunChange(CurrencyRepository repo, CurrencyData currency, string currencyKey, bool limited)
    {
        while (true)
        {
            Console.Write("Cantidad en centimos (0, volver, limite, cercano o salir): ");
            string input = (Console.ReadLine() ?? string.Empty).Trim();
            string command = TextKey.Normalize(input);

            if (command == "salir")
            {
                return false;
            }

            if (command is "volver" or "0")
            {
                return true;
            }

            if (command is "limite" or "limit" or "l" or "cercano" or "nearest")
            {
                if (!RunAdvancedChange(repo, currency, currencyKey, limited,
                        command is "limite" or "limit" or "l"))
                    return false;
                continue;
            }

            if (!BigInteger.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger amount) || amount < 0)
            {
                Console.WriteLine("Entrada invalida. Introduzca un entero no negativo.");
                continue;
            }

            bool ok;
            List<BigInteger> solution;

            if (limited)
            {
                ok = ChangeCalculator.ComputeLimited(amount, currency.Denominations, currency.Stock,
                    out solution, out _);
                if (ok)
                {
                    if (!repo.TryConsumeStock(currencyKey, solution, out List<BigInteger> updatedStock))
                    {
                        Console.WriteLine("El stock ha cambiado o no se pudo guardar. Reintentelo.");
                        currency.Stock = repo.LoadStock(currencyKey) ?? currency.Stock;
                        continue;
                    }
                    currency.Stock = updatedStock;
                }
            }
            else
            {
                ok = ChangeCalculator.ComputeUnlimited(amount, currency.Denominations, out solution);
            }

            if (!ok)
            {
                Console.WriteLine("No existe cambio exacto para esa cantidad.");
                continue;
            }

            // Compatibilidad con PROGVORAZ_JSON del programa C.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROGVORAZ_JSON")))
            {
                object[] entries = currency.Denominations.Select((denomination, i) =>
                    limited
                        ? (object)new {
                            denominacion = denomination.ToString(CultureInfo.InvariantCulture),
                            cantidad = solution[i].ToString(CultureInfo.InvariantCulture),
                            stock = currency.Stock[i].ToString(CultureInfo.InvariantCulture)
                        }
                        : new {
                            denominacion = denomination.ToString(CultureInfo.InvariantCulture),
                            cantidad = solution[i].ToString(CultureInfo.InvariantCulture)
                        }).ToArray();
                Console.WriteLine(JsonSerializer.Serialize(new { resultado = entries }));
                continue;
            }

            Console.WriteLine("+--------------------------------------------------+");
            Console.WriteLine("Resultado del cambio");
            Console.WriteLine("+--------------------------------------------------+");
            for (int i = 0; i < currency.Denominations.Count; i++)
            {
                if (limited)
                {
                    Console.WriteLine($"Moneda {currency.Denominations[i]} c -> cantidad {solution[i]} | stock {currency.Stock[i]}");
                }
                else
                {
                    Console.WriteLine($"Moneda {currency.Denominations[i]} c -> cantidad {solution[i]}");
                }
            }
            Console.WriteLine("+--------------------------------------------------+");
        }
    }

    // The reference C console permits N, =N and N-M exact coin constraints.
    private static bool ParseCoinRestriction(string raw, out int minimum, out int maximum)
    {
        minimum = maximum = 0;
        string text = new(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (text.Length == 0) return false;
        if (text[0] == '=')
            return int.TryParse(text[1..], NumberStyles.None,
                CultureInfo.InvariantCulture, out minimum) &&
                (maximum = minimum) >= 0;
        int separator = text.IndexOf('-');
        if (separator >= 0)
        {
            return int.TryParse(text[..separator], NumberStyles.None,
                    CultureInfo.InvariantCulture, out minimum)
                && int.TryParse(text[(separator + 1)..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out maximum)
                && minimum <= maximum;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture,
            out maximum);
    }

    private static bool RunAdvancedChange(CurrencyRepository repository,
        CurrencyData currency, string currencyKey, bool limited, bool withConstraint)
    {
        int min = 0;
        int? max = null;
        if (withConstraint)
        {
            Console.Write("Restricción de monedas (N, =N, N-M, volver): ");
            string? restriction = Console.ReadLine();
            if (restriction is null) return false;
            if (TextKey.Normalize(restriction) is "volver" or "back") return true;
            if (!ParseCoinRestriction(restriction, out min, out int upper))
            {
                Console.WriteLine("Restricción inválida.");
                return true;
            }
            max = upper;
        }
        Console.Write("Cantidad en céntimos (volver o salir): ");
        string? rawAmount = Console.ReadLine();
        if (rawAmount is null) return false;
        string command = TextKey.Normalize(rawAmount);
        if (command is "salir" or "exit" or "quit") return false;
        if (command is "volver" or "back") return true;
        if (!BigInteger.TryParse(rawAmount, NumberStyles.None, CultureInfo.InvariantCulture,
                out BigInteger amount) || amount <= 0)
        {
            Console.WriteLine("Importe inválido.");
            return true;
        }
        try
        {
            var answer = EnhancedChangeEngine.Compute(amount, currency.Denominations,
                limited ? currency.Stock : null, min, max, nearest: false);
            bool closeSuggestion = false;
            if (answer is null)
            {
                answer = EnhancedChangeEngine.Compute(amount, currency.Denominations,
                    limited ? currency.Stock : null, min, max, nearest: true);
                closeSuggestion = answer is not null && answer.Covered < amount;
            }
            if (answer is null)
            {
                Console.WriteLine("No existe cambio exacto ni sugerencia cercana positiva.");
                return true;
            }
            if (closeSuggestion)
            {
                Console.WriteLine($"Sugerencia cercana: cubrir {answer.Covered} c " +
                    $"(faltan {amount - answer.Covered} c).");
                Console.Write("¿Aceptar sugerencia? (si/no): ");
                string? confirmation = Console.ReadLine();
                if (confirmation is null) return false;
                if (TextKey.Normalize(confirmation) is not ("si" or "sí" or "s" or "y" or "yes"))
                {
                    Console.WriteLine("Sugerencia rechazada; el stock no se modifica.");
                    return true;
                }
            }
            if (limited)
            {
                if (!repository.TryConsumeStock(currencyKey, answer.Counts,
                        out List<BigInteger> updated))
                {
                    Console.WriteLine("El stock cambió o no pudo guardarse.");
                    currency.Stock = repository.LoadStock(currencyKey) ?? currency.Stock;
                    return true;
                }
                currency.Stock = updated;
            }
            Console.WriteLine($"Cambio aceptado: {answer.Covered} céntimos; " +
                $"piezas={answer.TotalCoins}.");
            for (int i = 0; i < answer.Counts.Length; i++)
            {
                Console.WriteLine($"Denominación={currency.Denominations[i]} " +
                    $"Cantidad={answer.Counts[i]}" +
                    (limited ? $" Stock={currency.Stock[i]}" : ""));
            }
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine("Búsqueda no soportada: " + ex.Message);
        }
        return true;
    }

    private static bool RunAdmin(CurrencyRepository repo, CurrencyData currency, string currencyKey)
    {
        while (true)
        {
            Console.WriteLine("+--------------------------------------------------+");
            Console.WriteLine("Panel Administrador (Consola)");
            Console.WriteLine("+--------------------------------------------------+");
            for (int i = 0; i < currency.Denominations.Count; i++)
            {
                Console.WriteLine($"[{i + 1}] Denom {currency.Denominations[i]} c -> stock {currency.Stock[i]}");
            }

            Console.Write("Accion admin (anadir/quitar, volver, modo, salir): ");
            string action = TextKey.Normalize((Console.ReadLine() ?? string.Empty).Trim());
            if (action == "salir")
            {
                return false;
            }

            if (action is "volver" or "modo")
            {
                return true;
            }

            if (action is not "anadir" and not "quitar")
            {
                Console.WriteLine("Accion invalida.");
                continue;
            }

            bool add = action == "anadir";

            Console.Write($"Indice de denominacion (1-{currency.Denominations.Count}): ");
            if (!int.TryParse(Console.ReadLine(), out int idx) || idx < 1 || idx > currency.Denominations.Count)
            {
                Console.WriteLine("Indice invalido.");
                continue;
            }

            Console.Write("Cantidad: ");
            if (!BigInteger.TryParse(Console.ReadLine(), NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger delta) || delta < 0)
            {
                Console.WriteLine("Cantidad invalida.");
                continue;
            }

            int index = idx - 1;
            if (!add && currency.Stock[index] < delta)
            {
                Console.WriteLine("No se puede quitar mas stock del disponible.");
                continue;
            }

            if (repo.TryAdjustStock(currencyKey, index, delta, add, out List<BigInteger> freshStock))
            {
                currency.Stock = freshStock;
                Console.WriteLine("Stock actualizado correctamente.");
            }
            else
            {
                currency.Stock = repo.LoadStock(currencyKey) ?? currency.Stock;
                Console.WriteLine("No se pudo actualizar el inventario. Vuelva a intentarlo.");
            }
        }
    }

    private static string ReadValue(string message)
    {
        Console.Write(message);
        return Console.ReadLine()?.Trim()
            ?? throw new EndOfStreamException("Se terminó la entrada.");
    }

    private static BigInteger[] ReadCoinCounts(string raw, int n)
    {
        string[] tokens = raw.Split(',');
        if (tokens.Length != n)
            throw new ArgumentException($"Se requieren exactamente {n} cantidades.");
        BigInteger[] values = new BigInteger[n];
        for (int i = 0; i < n; ++i)
            if (!BigInteger.TryParse(tokens[i], NumberStyles.None,
                CultureInfo.InvariantCulture, out values[i]) || values[i] < 0)
                throw new ArgumentException($"Cantidad incorrecta en posición {i + 1}.");
        return values;
    }

    private static bool ExecuteMenuCommand(PortableServices services, string command)
    {
        switch (command)
        {
            case "historial":
            case "history":
                Console.WriteLine("--- HISTORIAL DE TRANSACCIONES ---");
                var history = services.History(30);
                if (history.Count == 0) Console.WriteLine("No hay transacciones registradas.");
                foreach (string entry in history) Console.WriteLine(entry);
                return true;
            case "resumen":
            case "summary":
                Console.WriteLine(services.CurrencySummary(ReadValue("Moneda: ")));
                return true;
            case "snapshot":
                services.Snapshot(ReadValue("Ruta para la copia de stock: "));
                Console.WriteLine("Copia de stock completada.");
                return true;
            case "restaurar":
            case "restore":
                services.Restore(ReadValue("Ruta del snapshot de stock: "));
                Console.WriteLine("Inventario restaurado.");
                return true;
            case "reporte":
            case "report":
                services.ExportReport(ReadValue("Ruta del reporte: "));
                Console.WriteLine("Reporte generado.");
                return true;
            case "json":
                services.ExportJson(ReadValue("Ruta del JSON: "));
                Console.WriteLine("Inventario exportado a JSON.");
                return true;
            case "x":
            case "convertir":
            case "convert":
                string from = ReadValue("Moneda de origen: ");
                string to = ReadValue("Moneda de destino: ");
                string raw = ReadValue("Importe en céntimos: ");
                if (!BigInteger.TryParse(raw, NumberStyles.None,
                    CultureInfo.InvariantCulture, out BigInteger amount) || amount < 0)
                    throw new ArgumentException("El importe no es válido.");
                decimal rate = ExchangeRates.GetRateAsync(from, to,
                    Environment.CurrentDirectory).GetAwaiter().GetResult();
                BigInteger converted = ExchangeRates.ConvertCents(amount, rate);
                string target = PortableServices.MapCurrency(to);
                CurrencyData data = services.Repository.LoadCurrency(target)
                    ?? throw new ArgumentException("La moneda de destino no existe.");
                var solution = EnhancedChangeEngine.Compute(converted, data.Denominations);
                if (solution is null)
                    throw new InvalidOperationException("El importe de destino no admite desglose exacto.");
                Console.WriteLine($"Conversión aplicada: {amount} -> {converted} céntimos; tasa={rate}");
                for (int i = 0; i < solution.Counts.Length; ++i)
                    Console.WriteLine($"{data.Denominations[i]}: {solution.Counts[i]}");
                return true;
            case "caja":
            case "specific":
                string key = PortableServices.MapCurrency(ReadValue("Moneda de caja: "));
                CurrencyData current = services.Repository.LoadCurrency(key)
                    ?? throw new ArgumentException("Moneda desconocida.");
                Console.WriteLine("Denominaciones: " + string.Join(", ", current.Denominations));
                BigInteger[] incoming = ReadCoinCounts(
                    ReadValue("Cantidades recibidas, separadas por comas: "), current.Denominations.Count);
                BigInteger[] outgoing = ReadCoinCounts(
                    ReadValue("Cantidades entregadas, separadas por comas: "), current.Denominations.Count);
                bool useStock = ReadValue("¿Actualizar stock? (s/n): ").ToLowerInvariant() is "s" or "si" or "sí" or "y" or "yes";
                SpecificExchange.Result? exchange = SpecificExchange.Execute(
                    services.Repository, key, incoming, outgoing, useStock);
                if (exchange is null)
                    throw new InvalidOperationException("Cambio rechazado por importe desigual o stock insuficiente.");
                Console.WriteLine($"Cambio específico correcto. Valor={exchange.TotalCents} céntimos.");
                return true;
            default:
                return false;
        }
    }

    private static int AskMode(PortableServices services)
    {
        while (true)
        {
            Console.WriteLine("+--------------------------------------------------+");
            Console.WriteLine("|          SISTEMA DE CAMBIO DE MONEDAS            |");
            Console.WriteLine("+--------------------------------------------------+");
            Console.WriteLine("| Elija modo de trabajo:                            |");
            Console.WriteLine("|   a) Monedas infinitas                            |");
            Console.WriteLine("|   b) Monedas limitadas (usa stock)                |");
            Console.WriteLine("|   c) Administrador de stock                       |");
            Console.WriteLine("|   x) Conversión de monedas                        |");
            Console.WriteLine("|   caja, historial, resumen, snapshot, restaurar   |");
            Console.WriteLine("|   reporte, json (también en inglés)               |");
            Console.WriteLine("+--------------------------------------------------+");
            Console.Write("Opcion (o 'salir'): ");
            string? line = Console.ReadLine();
            if (line is null) return 0;
            string input = TextKey.Normalize(line.Trim());
            if (input is "salir" or "exit" or "quit") return 0;
            if (input is "a" or "b" or "c") return input[0];

            try
            {
                if (ExecuteMenuCommand(services, input)) continue;
            }
            catch (Exception error) when (error is IOException or ArgumentException
                or InvalidDataException or InvalidOperationException or
                NotSupportedException or UnauthorizedAccessException or HttpRequestException)
            {
                Console.WriteLine("Operación no completada: " + error.Message);
                continue;
            }
            Console.WriteLine("Opción no válida.");
        }
    }

    private static string? FindDataRoot()
    {
        DirectoryInfo? dir = new(Environment.CurrentDirectory);
        while (dir is not null)
        {
            string monedas = Path.Combine(dir.FullName, "monedas.txt");
            string stock = Path.Combine(dir.FullName, "stock.txt");
            if (File.Exists(monedas))
            {
                // A Win32 atomic replace may briefly make stock.txt appear
                // absent/inaccessible to File.Exists. Retry only this initial
                // discovery, not a monetary transaction. The repository also
                // retries its actual file open after selecting this directory.
                int attempts = OperatingSystem.IsWindows() ? 200 : 1;
                for (int i = 0; i < attempts; i++)
                {
                    if (File.Exists(stock)) return dir.FullName;
                    if (i + 1 < attempts) Thread.Sleep(10);
                }
            }

            dir = dir.Parent;
        }

        return null;
    }
}
