using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace ProgVorazCs.Core;

/// <summary>
/// Tipos de cambio deterministas (rates_stub.json) y fuente HTTP opcional.
/// No se interpreta contenido JSON mediante búsqueda de substrings.
/// </summary>
public static class ExchangeRates
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly IReadOnlyDictionary<string, string> Codes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["euro"] = "EUR", ["dolar"] = "USD", ["yen"] = "JPY",
            ["libra"] = "GBP", ["franco_suizo"] = "CHF",
            ["dolar_canadiense"] = "CAD", ["dolar_australiano"] = "AUD",
            ["yuan_chino"] = "CNY", ["peso_mexicano"] = "MXN"
        };

    public static string IsoCode(string name)
    {
        string normalized = PortableServices.MapCurrency(name);
        if (Codes.TryGetValue(normalized, out string? iso))
            return iso;
        if (normalized.Length == 3 && normalized.All(c => c is >= 'a' and <= 'z'))
            return normalized.ToUpperInvariant();
        throw new ArgumentException("Moneda sin codigo ISO reconocido: " + name);
    }

    public static async Task<decimal> GetRateAsync(string from, string to, string root,
        CancellationToken cancellationToken = default)
    {
        string sourceIso = IsoCode(from);
        string destinationIso = IsoCode(to);
        if (sourceIso == destinationIso) return 1m;

        // Modo stub: reproducible, sin red, utilizado en CI.
        if (string.Equals(Environment.GetEnvironmentVariable("PROGVORAZ_EXCHANGE_SOURCE"),
            "stub", StringComparison.OrdinalIgnoreCase))
            return LoadStub(sourceIso, destinationIso, root);

        // Replica el proveedor opcional del original en C. Si la API exige una
        // clave o devuelve error, se usa la configuración local sin inventar tasas.
        try
        {
            string endpoint = "https://api.exchangerate.host/convert?from="
                + Uri.EscapeDataString(sourceIso) + "&to="
                + Uri.EscapeDataString(destinationIso) + "&amount=1";
            using HttpResponseMessage response = await Client.GetAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("result", out JsonElement result)
                && result.ValueKind == JsonValueKind.Number
                && result.TryGetDecimal(out decimal value) && value > 0)
                return value;
        }
        catch (Exception exception) when (exception is HttpRequestException or
            TaskCanceledException or JsonException or IOException or OverflowException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        return LoadStub(sourceIso, destinationIso, root);
    }

    public static decimal LoadStub(string fromIso, string toIso, string root)
    {
        string path = Path.Combine(root, "rates_stub.json");
        if (!File.Exists(path))
            throw new InvalidDataException("No existe una tasa disponible: " + fromIso + "_" + toIso);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty(fromIso + "_" + toIso, out JsonElement rate)
            || rate.ValueKind != JsonValueKind.Number || !rate.TryGetDecimal(out decimal value)
            || value <= 0)
            throw new InvalidDataException("Tasa ausente o invalida: " + fromIso + "_" + toIso);
        return value;
    }

    /// <summary>
    /// Multiplicación y redondeo de BigInteger mediante la escala decimal exacta.
    /// Equivale a floor(amount * rate + 0.5) para cantidades no negativas.
    /// </summary>
    public static BigInteger ConvertCents(BigInteger amount, decimal rate)
    {
        if (amount < 0 || rate <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int[] bits = decimal.GetBits(rate);
        BigInteger significand = ((BigInteger)(uint)bits[2] << 64)
            | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        int scale = (bits[3] >> 16) & 0xff;
        BigInteger divisor = BigInteger.Pow(10, scale);
        return (amount * significand * 2 + divisor) / (divisor * 2);
    }
}
