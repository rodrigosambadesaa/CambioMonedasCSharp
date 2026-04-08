using System.Globalization;
using System.Numerics;
using ProgVorazCs.Core;

internal static class Program
{
    private static int Main()
    {
        string? dataRoot = FindDataRoot();
        if (dataRoot is null)
        {
            Console.WriteLine("No se encontraron monedas.txt y stock.txt.");
            return 1;
        }

        CurrencyRepository repo = new(dataRoot);
        while (true)
        {
            int mode = AskMode();
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

            CurrencyData? currency = repo.LoadCurrency(command);
            if (currency is null)
            {
                Console.WriteLine("No se pudo cargar moneda/stock para esa clave.");
                continue;
            }

            if (mode == 'c')
            {
                if (!RunAdmin(repo, currency, command))
                {
                    return false;
                }

                continue;
            }

            if (!RunChange(repo, currency, command, mode == 'b'))
            {
                return false;
            }
        }
    }

    private static bool RunChange(CurrencyRepository repo, CurrencyData currency, string currencyKey, bool limited)
    {
        while (true)
        {
            Console.Write("Cantidad en centimos (0, volver o salir): ");
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

            if (!BigInteger.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger amount) || amount < 0)
            {
                Console.WriteLine("Entrada invalida. Introduzca un entero no negativo.");
                continue;
            }

            bool ok;
            List<BigInteger> solution;

            if (limited)
            {
                ok = ChangeCalculator.ComputeLimited(amount, currency.Denominations, currency.Stock, out solution, out List<BigInteger> updatedStock);
                if (ok)
                {
                    currency.Stock = updatedStock;
                    _ = repo.UpdateStock(currencyKey, updatedStock);
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

            currency.Stock[index] = add ? currency.Stock[index] + delta : currency.Stock[index] - delta;
            Console.WriteLine(repo.UpdateStock(currencyKey, currency.Stock) ? "Stock actualizado correctamente." : "No se pudo actualizar stock.txt.");
        }
    }

    private static int AskMode()
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
            Console.WriteLine("+--------------------------------------------------+");
            Console.Write("Opcion (o 'salir'): ");
            string input = TextKey.Normalize((Console.ReadLine() ?? string.Empty).Trim());

            if (input == "salir")
            {
                return 0;
            }

            if (input.Length > 0 && (input[0] == 'a' || input[0] == 'b' || input[0] == 'c'))
            {
                return input[0];
            }

            Console.WriteLine("Opcion no valida.");
        }
    }

    private static string? FindDataRoot()
    {
        DirectoryInfo? dir = new(Environment.CurrentDirectory);
        while (dir is not null)
        {
            string monedas = Path.Combine(dir.FullName, "monedas.txt");
            string stock = Path.Combine(dir.FullName, "stock.txt");
            if (File.Exists(monedas) && File.Exists(stock))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
