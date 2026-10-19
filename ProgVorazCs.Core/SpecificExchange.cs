using System.Numerics;

namespace ProgVorazCs.Core;

/// <summary>
/// Port of the 'caja / especifico' flow of the original C application.
/// A manual exchange is valid only if the monetary values match exactly.
/// </summary>
public static class SpecificExchange
{
    public sealed record Result(BigInteger TotalCents, IReadOnlyList<BigInteger> CurrentStock);

    public static Result? Execute(CurrencyRepository repository, string currencyName,
        IReadOnlyList<BigInteger> received, IReadOnlyList<BigInteger> returned,
        bool updateStock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(returned);

        CurrencyData data = repository.LoadCurrency(currencyName)
            ?? throw new ArgumentException("Moneda desconocida o inconsistente.", nameof(currencyName));

        int count = data.Denominations.Count;
        if (received.Count != count || returned.Count != count ||
            received.Any(x => x < 0) || returned.Any(x => x < 0))
            throw new ArgumentException("Las cantidades deben ser no negativas y coincidir con las denominaciones.");

        BigInteger incoming = BigInteger.Zero, outgoing = BigInteger.Zero;
        for (int i = 0; i < count; i++)
        {
            incoming += data.Denominations[i] * received[i];
            outgoing += data.Denominations[i] * returned[i];
        }
        if (incoming != outgoing)
            return null;

        if (!updateStock) return new Result(incoming, data.Stock);
        return repository.TryExchangeStock(currencyName, received, returned, out List<BigInteger> balance)
            ? new Result(incoming, balance) : null;
    }
}
