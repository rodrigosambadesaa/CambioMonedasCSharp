using System.Globalization;
using System.Numerics;
using ProgVorazCs.Core;

namespace ProgVorazCs.ConsoleApp;

/// <summary>
/// Portable terminal control panel equivalent to gui_portable.c: unlike WinForms,
/// it is usable on Linux and macOS without loading any native C executable.
/// </summary>
public static class GuiPortable
{
    public static int Run(string dataRoot)
    {
        CurrencyRepository repository = new(dataRoot);
        PortableServices services = new(dataRoot);
        while (true)
        {
            System.Console.Write("GUI portable - modo (limitado/ilimitado/salir): ");
            string? mode = Read();
            if (mode is null || IsExit(mode)) return 0;
            bool limited;
            if (mode is "limitado" or "b") limited = true;
            else if (mode is "ilimitado" or "a") limited = false;
            else { System.Console.WriteLine("Modo no valido."); continue; }

            System.Console.WriteLine("Monedas: " + string.Join(", ", repository.LoadCurrencyNames()));
            System.Console.Write("Moneda (o volver): ");
            string? requested = Read();
            if (requested is null || IsExit(requested)) return 0;
            if (requested == "volver") continue;
            string key = PortableServices.MapCurrency(requested);
            CurrencyData? currency = repository.LoadCurrency(key);
            if (currency is null || !services.Validate(key))
            {
                System.Console.WriteLine("Moneda inexistente o inventario inconsistente.");
                continue;
            }

            while (true)
            {
                currency = repository.LoadCurrency(key);
                if (currency is null)
                {
                    System.Console.WriteLine("No se puede cargar el inventario.");
                    break;
                }
                System.Console.WriteLine("Moneda=" + key + " Modo=" + (limited ? "limitado" : "ilimitado"));
                for (int i = 0; i < currency.Denominations.Count; ++i)
                    System.Console.WriteLine((i + 1) + ") " + currency.Denominations[i] +
                        " centimos, stock=" + currency.Stock[i]);
                System.Console.Write("Accion (calcular/cercano/anadir/quitar/especifico/resumen/historial/volver/salir): ");
                string? action = Read();
                if (action is null || IsExit(action)) return 0;
                if (action == "volver") break;
                try
                {
                    switch (action)
                    {
                        case "calcular": Calculate(repository, currency, limited, false); break;
                        case "cercano": Calculate(repository, currency, limited, true); break;
                        case "anadir": Adjust(repository, currency, true); break;
                        case "quitar": Adjust(repository, currency, false); break;
                        case "especifico": Specific(repository, currency, limited); break;
                        case "resumen": System.Console.WriteLine(services.CurrencySummary(key)); break;
                        case "historial":
                            foreach (string line in services.History(20)) System.Console.WriteLine(line);
                            break;
                        default: System.Console.WriteLine("Accion no valida."); break;
                    }
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or
                    InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
                {
                    System.Console.WriteLine("Operacion rechazada: " + ex.Message);
                }
            }
        }
    }

    private static string? Read() =>
        System.Console.ReadLine() is { } line ? TextKey.Normalize(line.Trim()) : null;

    private static bool IsExit(string value) => value is "salir" or "exit" or "quit";

    private static bool ReadUnsigned(string prompt, out BigInteger value)
    {
        System.Console.Write(prompt);
        string? raw = System.Console.ReadLine();
        if (raw is null) { value = BigInteger.Zero; return false; }
        return BigInteger.TryParse(raw.Trim(), NumberStyles.None,
            CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static void Calculate(CurrencyRepository repository, CurrencyData currency,
        bool limited, bool nearest)
    {
        if (!ReadUnsigned("Importe en centimos: ", out BigInteger amount))
        {
            System.Console.WriteLine("Importe invalido.");
            return;
        }
        EnhancedChangeEngine.Answer? answer = EnhancedChangeEngine.Compute(amount,
            currency.Denominations, limited ? currency.Stock : null, nearest: nearest);
        if (answer is null)
        {
            System.Console.WriteLine("No existe cambio con el inventario solicitado.");
            return;
        }
        if (nearest && answer.Covered != amount)
        {
            System.Console.WriteLine("Cambio cercano propuesto: " + answer.Covered + " centimos.");
            System.Console.Write("Aceptar (si/no): ");
            if (Read() is not ("si" or "s" or "yes")) return;
        }
        if (limited && !repository.TryConsumeStock(currency.Name, answer.Counts, out _))
        {
            System.Console.WriteLine("El stock ha cambiado: operacion rechazada sin consumo parcial.");
            return;
        }
        System.Console.WriteLine("Cambio calculado: " + answer.Covered +
            " centimos; piezas=" + answer.TotalCoins);
        for (int i = 0; i < answer.Counts.Length; ++i)
            if (!answer.Counts[i].IsZero)
                System.Console.WriteLine(currency.Denominations[i] + " x " + answer.Counts[i]);
    }

    private static void Adjust(CurrencyRepository repository, CurrencyData currency, bool add)
    {
        if (!ReadUnsigned("Indice de denominacion (1-N): ", out BigInteger index) ||
            index < 1 || index > currency.Denominations.Count ||
            !ReadUnsigned("Cantidad de piezas: ", out BigInteger count))
        {
            System.Console.WriteLine("Indice o cantidad invalida.");
            return;
        }
        if (!repository.TryAdjustStock(currency.Name, (int)index - 1, count, add, out _))
        {
            System.Console.WriteLine("Operacion rechazada: stock insuficiente o conflicto.");
            return;
        }
        System.Console.WriteLine("Inventario actualizado.");
    }

    private static BigInteger[]? ReadCounts(int length, string label)
    {
        System.Console.Write(label + " (" + length + " numeros separados por coma): ");
        string? line = System.Console.ReadLine();
        if (line is null) return null;
        string[] parts = line.Split(',');
        if (parts.Length != length) return null;
        BigInteger[] result = new BigInteger[length];
        for (int i = 0; i < parts.Length; ++i)
            if (!BigInteger.TryParse(parts[i].Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out result[i]) || result[i] < 0)
                return null;
        return result;
    }

    private static void Specific(CurrencyRepository repository, CurrencyData currency, bool limited)
    {
        int count = currency.Denominations.Count;
        BigInteger[]? received = ReadCounts(count, "Recibidas");
        BigInteger[]? returned = ReadCounts(count, "Devueltas");
        if (received is null || returned is null)
        {
            System.Console.WriteLine("Cantidades no validas.");
            return;
        }
        BigInteger incoming = BigInteger.Zero, outgoing = BigInteger.Zero;
        for (int i = 0; i < count; ++i)
        {
            incoming += currency.Denominations[i] * received[i];
            outgoing += currency.Denominations[i] * returned[i];
        }
        if (incoming != outgoing)
        {
            System.Console.WriteLine("Cambio especifico rechazado: valores diferentes.");
            return;
        }
        if (limited && !repository.TryExchangeStock(currency.Name, received, returned, out _))
        {
            System.Console.WriteLine("Cambio especifico rechazado: inventario insuficiente.");
            return;
        }
        System.Console.WriteLine("Cambio especifico correcto. Valor=" + incoming);
    }
}
