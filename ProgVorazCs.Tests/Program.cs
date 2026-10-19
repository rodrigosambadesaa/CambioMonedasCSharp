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

Match(6, [4,3,1], null, 0, null, true, 2);
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
