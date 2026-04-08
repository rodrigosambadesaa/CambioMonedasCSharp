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

public static class TextKey
{
	public static string Normalize(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		string normalized = text.Trim().Normalize(NormalizationForm.FormD);
		StringBuilder sb = new();
		foreach (char c in normalized)
		{
			UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
			if (category != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(char.ToLowerInvariant(c));
			}
		}

		return sb.ToString().Normalize(NormalizationForm.FormC);
	}
}

public sealed class CurrencyRepository
{
	private readonly string monedasPath;
	private readonly string stockPath;

	public CurrencyRepository(string dataRoot)
	{
		monedasPath = Path.Combine(dataRoot, "monedas.txt");
		stockPath = Path.Combine(dataRoot, "stock.txt");
	}

	public IReadOnlyList<string> LoadCurrencyNames()
	{
		List<string> tokens = ReadTokens(monedasPath);
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		List<string> names = [];

		foreach (string token in tokens)
		{
			if (IsNonNegativeInteger(token))
			{
				continue;
			}

			if (seen.Add(token))
			{
				names.Add(token);
			}
		}

		return names;
	}

	public List<BigInteger>? LoadDenominations(string currencyName)
	{
		return LoadCurrencyNumbers(monedasPath, currencyName, convertMinusOneToZero: false);
	}

	public List<BigInteger>? LoadStock(string currencyName)
	{
		return LoadCurrencyNumbers(stockPath, currencyName, convertMinusOneToZero: true);
	}

	public bool UpdateStock(string currencyName, IReadOnlyList<BigInteger> stock)
	{
		if (!File.Exists(stockPath) || stock.Count == 0)
		{
			return false;
		}

		List<string> lines = File.ReadAllLines(stockPath).ToList();
		string target = TextKey.Normalize(currencyName);

		for (int i = 0; i < lines.Count; i++)
		{
			string lineKey = TextKey.Normalize(lines[i]);
			if (!string.Equals(lineKey, target, StringComparison.Ordinal))
			{
				continue;
			}

			if (i + stock.Count >= lines.Count + 1)
			{
				return false;
			}

			for (int j = 0; j < stock.Count; j++)
			{
				BigInteger value = stock[j];
				lines[i + 1 + j] = value.IsZero ? "-1" : value.ToString(CultureInfo.InvariantCulture);
			}

			File.WriteAllLines(stockPath, lines);
			return true;
		}

		return false;
	}

	public CurrencyData? LoadCurrency(string currencyName)
	{
		List<BigInteger>? denominations = LoadDenominations(currencyName);
		List<BigInteger>? stock = LoadStock(currencyName);
		if (denominations is null || stock is null || denominations.Count != stock.Count)
		{
			return null;
		}

		return new CurrencyData
		{
			Name = currencyName,
			Denominations = denominations,
			Stock = stock,
		};
	}

	private static List<string> ReadTokens(string path)
	{
		if (!File.Exists(path))
		{
			return [];
		}

		List<string> tokens = [];
		foreach (string line in File.ReadLines(path))
		{
			string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			tokens.AddRange(parts);
		}

		return tokens;
	}

	private static List<BigInteger>? LoadCurrencyNumbers(string path, string currencyName, bool convertMinusOneToZero)
	{
		List<string> tokens = ReadTokens(path);
		string target = TextKey.Normalize(currencyName);

		bool inSection = false;
		List<BigInteger> values = [];

		foreach (string token in tokens)
		{
			if (!inSection)
			{
				if (TextKey.Normalize(token) == target)
				{
					inSection = true;
				}

				continue;
			}

			string t = token;
			if (convertMinusOneToZero && t == "-1")
			{
				t = "0";
			}

			if (!IsNonNegativeInteger(t))
			{
				break;
			}

			values.Add(BigInteger.Parse(t, CultureInfo.InvariantCulture));
		}

		return values.Count == 0 ? null : values;
	}

	private static bool IsNonNegativeInteger(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}

		foreach (char c in text)
		{
			if (c < '0' || c > '9')
			{
				return false;
			}
		}

		return true;
	}
}

public static class ChangeCalculator
{
	public static bool ComputeUnlimited(BigInteger amount, IReadOnlyList<BigInteger> denominations, out List<BigInteger> result)
	{
		result = Enumerable.Repeat(BigInteger.Zero, denominations.Count).ToList();
		if (amount < 0)
		{
			return false;
		}

		BigInteger remaining = amount;
		for (int i = 0; i < denominations.Count; i++)
		{
			BigInteger d = denominations[i];
			if (d <= 0)
			{
				continue;
			}

			BigInteger q = BigInteger.DivRem(remaining, d, out BigInteger r);
			result[i] = q;
			remaining = r;
			if (remaining.IsZero)
			{
				break;
			}
		}

		return remaining.IsZero;
	}

	public static bool ComputeLimited(
		BigInteger amount,
		IReadOnlyList<BigInteger> denominations,
		IReadOnlyList<BigInteger> stock,
		out List<BigInteger> result,
		out List<BigInteger> updatedStock)
	{
		result = Enumerable.Repeat(BigInteger.Zero, denominations.Count).ToList();
		updatedStock = stock.ToList();

		if (amount < 0 || denominations.Count != stock.Count)
		{
			return false;
		}

		BigInteger remaining = amount;
		for (int i = 0; i < denominations.Count; i++)
		{
			BigInteger d = denominations[i];
			if (d <= 0)
			{
				continue;
			}

			BigInteger q = BigInteger.DivRem(remaining, d, out _);
			BigInteger take = BigInteger.Min(q, updatedStock[i]);

			result[i] = take;
			remaining -= take * d;
			updatedStock[i] -= take;

			if (remaining.IsZero)
			{
				break;
			}
		}

		return remaining.IsZero;
	}
}
