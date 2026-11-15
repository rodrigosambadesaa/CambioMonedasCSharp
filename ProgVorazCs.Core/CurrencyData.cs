using System.Globalization;
using System.Numerics;
using System.Text;

namespace ProgVorazCs.Core;

public sealed class CurrencyData
{
    public required string Name { get; set; }
    public required List<BigInteger> Denominations { get; set; }
    public required List<BigInteger> Stock { get; set; }
}
