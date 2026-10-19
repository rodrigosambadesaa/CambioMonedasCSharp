using System.Globalization;
using System.Numerics;
using System.Text;

namespace ProgVorazCs.Core;

/// <summary>
/// Transacciones de inventario realizadas bajo bloqueo interproceso.
/// Las actualizaciones se hacen con reemplazo de archivo, no mediante
/// escritura truncada sobre el archivo de stock en uso.
/// </summary>
internal static class StockLedger
{
    private static FileStream Acquire(string path)
    {
        string lockFile = path + ".lock";
        for (int attempt = 0; attempt < 200; ++attempt)
        {
            try
            {
                return new FileStream(lockFile, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 199)
            {
                Thread.Sleep(10);
            }
        }
        throw new IOException("No se pudo adquirir el bloqueo de inventario.");
    }

    public static bool Replace(string path, string name, int expected,
        IReadOnlyList<BigInteger> values)
    {
        return Apply(path, name, expected,
            _ => values.Count == expected && values.All(v => v >= 0) ? values.ToArray() : null,
            out _);
    }

    public static bool Consume(string path, string name, int expected,
        IReadOnlyList<BigInteger> requested, out List<BigInteger> remainder)
    {
        return Apply(path, name, expected, current =>
        {
            if (requested.Count != expected || requested.Any(v => v < 0)) return null;
            for (int i = 0; i < expected; i++)
                if (requested[i] > current[i]) return null;
            return current.Select((amount, i) => amount - requested[i]).ToArray();
        }, out remainder);
    }

    public static bool Adjust(string path, string name, int expected, int index,
        BigInteger delta, bool add, out List<BigInteger> updated)
    {
        return Apply(path, name, expected, current =>
        {
            if (index < 0 || index >= expected || delta < 0 ||
                !add && current[index] < delta) return null;
            current[index] = add ? current[index] + delta : current[index] - delta;
            return current;
        }, out updated);
    }

    private static bool Apply(string path, string currency, int expected,
        Func<BigInteger[], BigInteger[]?> transform, out List<BigInteger> final)
    {
        final = [];
        if (expected <= 0 || !File.Exists(path)) return false;
        string? staging = null;
        try
        {
            using FileStream held = Acquire(path);
            string[] lines = File.ReadAllLines(path);
            string key = TextKey.Normalize(currency);
            int header = Array.FindIndex(lines, line => TextKey.Normalize(line) == key);
            if (header < 0) return false;

            int after = header + 1;
            List<BigInteger> values = [];
            while (after < lines.Length)
            {
                string token = lines[after].Trim();
                if (token == "-1") values.Add(BigInteger.Zero);
                else if (token.Length > 0 && token.All(c => c >= '0' && c <= '9'))
                    values.Add(BigInteger.Parse(token, NumberStyles.None, CultureInfo.InvariantCulture));
                else break;
                after++;
            }
            // Nunca escribir sobre el encabezado de la siguiente moneda.
            if (values.Count != expected) return false;
            BigInteger[]? newValues = transform(values.ToArray());
            if (newValues is null || newValues.Length != expected ||
                newValues.Any(v => v < 0)) return false;

            for (int i = 0; i < expected; ++i)
                lines[header + 1 + i] = newValues[i].IsZero ? "-1"
                    : newValues[i].ToString(CultureInfo.InvariantCulture);

            staging = Path.Combine(Path.GetDirectoryName(path)!,
                ".stock-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllLines(staging, lines, new UTF8Encoding(false));
            // File.Replace conserva el original si falla la escritura previa.
            File.Replace(staging, path, null);
            staging = null;
            final = newValues.ToList();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or FormatException)
        {
            return false;
        }
        finally
        {
            if (staging is not null && File.Exists(staging))
                File.Delete(staging);
        }
    }
}
