using System.Numerics;
using ProgVorazCs.Core;

static void Check(bool condition, string reason)
{
    if (!condition) throw new Exception(reason);
}

static void Match(BigInteger amount, int[] values, int[]? stock, int min, int? max, bool shouldExist, int? pieces = null)
{
    var denomination = values.Select(x => new BigInteger(x)).ToArray();
    var limits = stock?.Select(x => new BigInteger(x)).ToArray();
    var answer = EnhancedChangeEngine.Compute(amount, denomination, limits, min, max);
    Check((answer is not null) == shouldExist, "existencia: " + amount);
    if (answer is null) return;
    Check(answer.Counts.Length == values.Length, "longitud incorrecta");
    Check(answer.Counts.Zip(denomination, (a, b) => a*b).Aggregate(BigInteger.Zero, (a,b) => a+b) == amount, "suma incorrecta");
    Check(answer.TotalCoins >= min && (max is null || answer.TotalCoins <= max), "rango incorrecto");
    if (pieces is not null) Check(answer.TotalCoins == pieces, "cantidad no optima");
    if (limits is not null)
        Check(answer.Counts.Zip(limits, (a,b) => a <= b).All(x => x), "inventario superado");
}


// Oráculo independiente por enumeración exhaustiva de combinaciones.
// Contrasta la DP, el inventario, el rango de piezas y el importe cercano.
static (int Covered, int Pieces)? Oracle(int amount, int[] denominations,
    int[]? stock, int minCoins, int? maxCoins, bool nearest)
{
    int bestAmount = -1;
    int bestPieces = int.MaxValue;
    void Search(int index, int covered, int pieces)
    {
        if (covered > amount || maxCoins.HasValue && pieces > maxCoins.Value) return;
        if (index == denominations.Length)
        {
            if (pieces < minCoins || covered == 0 && nearest ||
                !nearest && covered != amount) return;
            if (covered > bestAmount || covered == bestAmount && pieces < bestPieces)
            {
                bestAmount = covered;
                bestPieces = pieces;
            }
            return;
        }
        int limit = (amount - covered) / denominations[index];
        if (stock is not null) limit = Math.Min(limit, stock[index]);
        for (int count = 0; count <= limit; ++count)
            Search(index + 1, covered + count * denominations[index], pieces + count);
    }
    Search(0, 0, 0);
    return bestAmount < 0 ? null : (bestAmount, bestPieces);
}

static void ExhaustiveDifferential()
{
    Random random = new(0xC0FFEE);
    for (int iteration = 0; iteration < 80; ++iteration)
    {
        int count = random.Next(2, 6);
        int[] denominations = Enumerable.Range(1, 10).OrderBy(_ => random.Next())
            .Take(count).OrderDescending().ToArray();
        int[] stock = Enumerable.Range(0, count).Select(_ => random.Next(0, 4)).ToArray();
        int amount = random.Next(0, 25);

        foreach (bool limited in new[] { false, true })
        {
            int[]? actualStock = limited ? stock : null;
            foreach (var cfg in new[] {
                (Min:0, Max:(int?)null, Near:false),
                (Min:2, Max:(int?)4, Near:false),
                (Min:1, Max:(int?)6, Near:true)
            })
            {
                var reference = Oracle(amount, denominations, actualStock, cfg.Min, cfg.Max, cfg.Near);
                var answer = EnhancedChangeEngine.Compute(amount,
                    denominations.Select(x => (BigInteger)x).ToArray(),
                    actualStock?.Select(x => (BigInteger)x).ToArray(),
                    cfg.Min, cfg.Max, cfg.Near);
                Check((reference is not null) == (answer is not null),
                    $"Diferencial existencia iter={iteration}, cantidad={amount}, limitado={limited}");
                if (answer is null || reference is null) continue;
                Check(answer.Covered == reference.Value.Covered
                    && answer.TotalCoins == reference.Value.Pieces,
                    $"Diferencial optimizacion iter={iteration}, cantidad={amount}, limitado={limited}");
                Check(answer.Counts.Zip(denominations,
                    (pieces, value) => pieces * value).Aggregate(BigInteger.Zero, (a, b) => a+b)
                    == answer.Covered, "Diferencial reconstruccion de valor");
                if (actualStock is not null)
                    Check(answer.Counts.Zip(actualStock, (a, b) => a <= b).All(x => x),
                        "Diferencial exceso de stock");
            }
        }
    }
}

ExhaustiveDifferential();
Match(6, [4,3,1], null, 0, null, true, 2);
Match(12345, [10000,5000,2000,1000,500,200,100,50,25,10,5,1],
      null, 1, 100, true, 7); // Regresión contra el programa C original.
Match(6, [4,3,1], [1,2,1], 0, null, true, 2);
Match(8, [5,3,1], [1,0,2], 0, null, false);
Match(6, [4,3,1], null, 3, 3, true, 3);
Match(6, [4,3,1], [1,2,0], 3, 3, false);
Match(6, [4,3,1], [0,2,0], 2, 2, true, 2);
Match(6, [4,3,1], [1,1,0], 0, null, false);
Match(0, [4,3,1], null, 0, null, true, 0);
Match(BigInteger.Parse("99999999999999999999999999999999999"), [5,1], null, 0, null, true);
var nearest = EnhancedChangeEngine.Compute(11, [new BigInteger(6), new BigInteger(4)], nearest: true);
Check(nearest?.Covered == 10, "importe cercano incorrecto");
var unreachableNear = EnhancedChangeEngine.Compute(150_000,
    [new BigInteger(200_001)], nearest: true);
Check(unreachableNear is null, "cercano sin cambio no nulo");
var nearLarge = EnhancedChangeEngine.Compute(150_001,
    [new BigInteger(50_000)], nearest: true);
Check(nearLarge?.Covered == 150_000 && nearLarge.TotalCoins == 3,
    "cercano alto calculado con una tabla DP");


string temp = Path.Combine(Path.GetTempPath(), "progvoraz-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    File.WriteAllText(Path.Combine(temp, "monedas.txt"), "euro\n5\n3\n1\ndolar\n10\n1\n");
    File.WriteAllText(Path.Combine(temp, "stock.txt"), "euro\n4\n2\n-1\ndolar\n1\n2\n");
    File.WriteAllText(Path.Combine(temp, "rates_stub.json"), "{\"EUR_USD\":1.09,\"USD_EUR\":0.917}");
    Check(ExchangeRates.LoadStub("EUR", "USD", temp) == 1.09m, "stub EUR USD");
    Check(ExchangeRates.LoadStub("USD", "EUR", temp) == 0.917m, "stub USD EUR");
    var service = new PortableServices(temp);
    Check(service.Validate("EUR"), "validacion EUR");
    // Validacion de fronteras de bloque: no sobrescribir otra divisa.
    Check(!service.Repository.UpdateStock("euro", [0,0,0,0]), "bloque con longitud incorrecta rechazado");
    Check(service.Repository.LoadStock("dolar")!.SequenceEqual([BigInteger.One, new BigInteger(2)]),
          "no corromper stock de otra moneda");
    // Dos operaciones simultaneas no deben perder actualizaciones.
    Task<bool>[] concurrent = Enumerable.Range(0, 12).Select(i =>
        Task.Run(() => service.Repository.TryAdjustStock("euro", 0, BigInteger.One, true, out _))).ToArray();
    Check((await Task.WhenAll(concurrent)).All(x => x), "incrementos concurrentes");
    Check(service.Repository.LoadStock("euro")![0] == 16, "sin lost updates");
    Check(!service.Repository.TryConsumeStock("euro", [17,0,0], out _), "sin stock negativo");
    Check(service.Repository.TryConsumeStock("euro", [12,0,0], out var balance),
          "consumo atomico");
    Check(balance[0] == 4, "descuento exacto");
    Check(service.Repository.TryAdjustStock("euro", 1, BigInteger.One, false, out balance),
          "administrador quita");
    Check(balance[1] == 1, "resultado de administrador");
    Check(service.Repository.TryAdjustStock("euro", 1, BigInteger.One, true, out balance),
          "administrador añade");
    Check(balance[1] == 2, "restauracion de delta");
    // Cambio especifico exacto: depositar cinco monedas de 1 y devolver una de 5.
    var exchange = SpecificExchange.Execute(service.Repository, "euro",
        [0,0,5], [1,0,0], updateStock: true);
    Check(exchange?.TotalCents == 5, "especifico valor equivalente");
    Check(exchange.CurrentStock.SequenceEqual(
        [new BigInteger(3), new BigInteger(2), new BigInteger(5)]),
        "especifico ingresa y descuenta en una transaccion");
    var snapshotExchange = service.Repository.LoadStock("euro")!.ToArray();
    Check(SpecificExchange.Execute(service.Repository, "euro",
        [0,2,0], [0,0,6], updateStock: true) is null,
        "especifico rechaza inventario insuficiente");
    Check(SpecificExchange.Execute(service.Repository, "euro",
        [0,0,1], [1,0,0], updateStock: true) is null,
        "especifico rechaza diferencia de importe");
    Check(service.Repository.LoadStock("euro")!.SequenceEqual(snapshotExchange),
        "los fallos del cambio especifico no mutan inventario");
    var unlimited = SpecificExchange.Execute(service.Repository, "euro",
        [0,0,5], [1,0,0], updateStock: false);
    Check(unlimited?.TotalCents == 5
        && service.Repository.LoadStock("euro")!.SequenceEqual(snapshotExchange),
        "especifico ilimitado sin efectos secundarios");
    Check(service.Repository.TryExchangeStock("euro",
        [1,0,0], [0,0,5], out var resetExchange), "inversa atomica");
    Check(resetExchange.SequenceEqual([new BigInteger(4),new BigInteger(2),BigInteger.Zero]),
        "restaura inventario para el resto de pruebas");
    service.Snapshot(Path.Combine(temp, "backup.txt"));
    var data = service.Repository.LoadCurrency("euro")!;
    Check(data.Stock.SequenceEqual([new BigInteger(4), new BigInteger(2), BigInteger.Zero]), "stock -1");
    using StringReader input = new("mode,moneda,monto,range\na,EUR,6,\nb,EUR,6,\na,EUR,6,3-3\n");
    using StringWriter output = new();
    service.RunBatch(input, output);
    string csv = output.ToString();
    Check(csv.Split("OK total=2").Length - 1 == 2 && csv.Contains(",NO,"),
        "resultado de batch");
    service.ExportJson(Path.Combine(temp, "stock.json"));
    Check(File.ReadAllText(Path.Combine(temp, "stock.json")).Contains("\"nombre\": \"euro\""), "export JSON");
    service.ExportReport(Path.Combine(temp, "report.txt"));
    Check(File.ReadAllText(Path.Combine(temp, "report.txt")).Contains("valor_centimos="), "reporte");
    Check(service.Repository.UpdateStock("euro", [0, 0, 0]), "actualizar stock");
    service.Restore(Path.Combine(temp, "backup.txt"));
    Check(service.Repository.LoadStock("euro")![0] == 4, "restaurar stock");

    // HTTP local parity: health, stock JSON, batch CSV, 404 y request demasiado grande.
    var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    socket.Start();
    int port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
    socket.Stop();
    using (var cancellation = new CancellationTokenSource())
    using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }))
    {
        Task server = LocalHttpServer.ServeAsync(service, port, cancellation.Token);
        try
        {
            string url = "http://127.0.0.1:" + port;
            Check(await client.GetStringAsync(url + "/health") == "OK", "HTTP health");
            string stockJson = await client.GetStringAsync(url + "/export_stock_json");
            Check(stockJson.Contains("\"nombre\": \"euro\""), "HTTP JSON");
            using var response = await client.PostAsync(url + "/batch",
                new StringContent("mode,moneda,monto\na,euro,6\n", System.Text.Encoding.UTF8, "text/csv"));
            Check(response.IsSuccessStatusCode, "HTTP batch status");
            Check((await response.Content.ReadAsStringAsync()).Contains("OK total=2"), "HTTP batch");
            var notFound = await client.GetAsync(url + "/missing");
            Check(notFound.StatusCode == System.Net.HttpStatusCode.NotFound, "HTTP 404");
            using var oversize = new ByteArrayContent(new byte[1024 * 1024 + 1]);
            var tooLarge = await client.PostAsync(url + "/batch", oversize);
            Check(tooLarge.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge, "HTTP 413");
        }
        finally
        {
            cancellation.Cancel();
            await server;
        }
    }
}
finally { Directory.Delete(temp, true); }
Check(ExchangeRates.IsoCode("USD") == "USD", "ISO USD");
Check(ExchangeRates.IsoCode("franco_suizo") == "CHF", "ISO CHF");
Check(ExchangeRates.ConvertCents(100, 1.09m) == 109, "EUR USD stub");
Check(ExchangeRates.ConvertCents(109, 0.917m) == 100, "USD EUR stub");
Check(ExchangeRates.ConvertCents(1, 0.5m) == 1, "redondeo medio");
Check(ExchangeRates.ConvertCents(BigInteger.Parse("999999999999999999999999999999"), 1m)
      == BigInteger.Parse("999999999999999999999999999999"), "no pierde precision BigInt");
try
{
    _ = ExchangeRates.ConvertCents(-1, 1m);
    throw new Exception("se acepto importe negativo");
}
catch (ArgumentOutOfRangeException) {}
Console.WriteLine("OK: motor, rango, inventario, BigInteger, cercano, batch, JSON, reporte, snapshot.");
