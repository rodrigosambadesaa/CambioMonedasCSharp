using System.Globalization;
using System.Numerics;
using System.Text;
using System.Runtime.InteropServices;

namespace ProgVorazCs.Core;

/// <summary>
/// Transacciones de inventario realizadas bajo bloqueo interproceso.
/// Las actualizaciones se hacen con reemplazo de archivo, no mediante
/// escritura truncada sobre el archivo de stock en uso.
/// </summary>
internal static class StockLedger
{
    // Use *native* open() on Unix: .NET FileStream's FileShare layer may
    // itself acquire advisory locks while opening, before our retry loop can
    // call flock. A plain POSIX fd matches the independent C++ implementation.
    private const int LockExclusive = 2, LockNonBlocking = 4, LockUnlock = 8;

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenNative(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags, int mode);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int CloseNative(int descriptor);

    private sealed class StockLease : IDisposable
    {
        private FileStream? windowsStream;
        private int unixDescriptor = -1;

        public StockLease(FileStream stream) { windowsStream = stream; }
        public StockLease(int descriptor) { unixDescriptor = descriptor; }

        public void Dispose()
        {
            if (unixDescriptor >= 0)
            {
                int fd = unixDescriptor;
                unixDescriptor = -1;
                try { _ = Flock(fd, LockUnlock); }
                finally { _ = CloseNative(fd); }
            }
            windowsStream?.Dispose();
            windowsStream = null;
        }
    }

    private static IDisposable Acquire(string path)
    {
        string lockFile = path + ".lock";
        if (OperatingSystem.IsWindows())
        {
            // C++ Windows opens its .lock file with CreateFileA share mode 0.
            for (int attempt = 0; attempt < 200; ++attempt)
            {
                try
                {
                    return new StockLease(new FileStream(lockFile, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None));
                }
                catch (IOException) when (attempt < 199) { Thread.Sleep(10); }
            }
            throw new IOException("No se pudo adquirir el bloqueo Win32 de inventario.");
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Se requiere POSIX flock o Windows.");

        // POSIX O_RDWR|O_CREAT differ on Linux and macOS; mode 0600.
        int create = OperatingSystem.IsMacOS() ? 0x0200 : 0x0040;
        int descriptor = OpenNative(lockFile, 2 | create, 0x180);
        if (descriptor < 0)
            throw new IOException("No se pudo abrir el archivo de bloqueo (errno=" +
                Marshal.GetLastPInvokeError() + ").");

        bool acquired = false;
        try
        {
            for (int attempt = 0; attempt < 200; ++attempt)
            {
                if (Flock(descriptor, LockExclusive | LockNonBlocking) == 0)
                {
                    acquired = true;
                    return new StockLease(descriptor);
                }
                int error = Marshal.GetLastPInvokeError();
                // EWOULDBLOCK=EAGAIN on Linux (11); EWOULDBLOCK=35 on macOS.
                if (error != 11 && error != 35)
                    throw new IOException("Error POSIX flock (errno=" + error + ").");
                Thread.Sleep(10);
            }
            throw new IOException("Tiempo de espera agotado para el bloqueo compartido.");
        }
        finally
        {
            if (!acquired) _ = CloseNative(descriptor);
        }
    }

    /// <summary>
    /// Execute a complete snapshot replacement under the SAME lock as
    /// consume, exchange, adjust and absolute stock writes.
    /// </summary>
    internal static void WithExclusiveLock(string stockPath, Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using IDisposable held = Acquire(stockPath);
        operation();
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

    /// <summary>
    /// Receive and dispense denominations within a single locked stock update.
    /// Incoming physical coins are available before outgoing coins are checked.
    /// </summary>
    public static bool Exchange(string path, string name, int expected,
        IReadOnlyList<BigInteger> received, IReadOnlyList<BigInteger> returned,
        out List<BigInteger> updated)
    {
        return Apply(path, name, expected, current =>
        {
            if (received.Count != expected || returned.Count != expected
                || received.Any(x => x < 0) || returned.Any(x => x < 0))
                return null;
            BigInteger[] next = new BigInteger[expected];
            for (int i = 0; i < expected; i++)
            {
                next[i] = current[i] + received[i] - returned[i];
                if (next[i] < 0) return null;
            }
            return next;
        }, out updated);
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
            using IDisposable held = Acquire(path);
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
            // Igual que el C original: el historial es informativo y su fallo
            // no deshace una transacción de stock ya consolidada.
            try
            {
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture) + "] Stock actualizado | Moneda=" +
                    currency.Replace('\n', '_').Replace('\r', '_') + Environment.NewLine;
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(path)!, "historial.txt"),
                    line, new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
