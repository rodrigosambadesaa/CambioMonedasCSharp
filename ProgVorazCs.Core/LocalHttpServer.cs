using System.Net;
using System.Text;

namespace ProgVorazCs.Core;

/// <summary>
/// Implementacion HTTP de los tres endpoints del servidor C.
/// Solo loopback, sin acceso externo, sin ficheros temporales compartidos.
/// </summary>
public static class LocalHttpServer
{
    private const int MaxRequestBytes = 1024 * 1024;

    public static async Task ServeAsync(PortableServices services, int port, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        using HttpListener listener = new();
        listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        listener.Start();
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().WaitAsync(cancel); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { break; }
                // HTTP requests are handled sequentially, as in the original server.
                await HandleAsync(services, context, cancel);
            }
        }
        finally { listener.Stop(); }
    }

    private static async Task HandleAsync(PortableServices services,
        HttpListenerContext context, CancellationToken cancel)
    {
        // Timeout is per accepted request, not a server-wide cancellation.
        // Otherwise a half-sent POST monopolizes the sequential listener.
        using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        requestDeadline.CancelAfter(TimeSpan.FromSeconds(8));
        CancellationToken requestToken = requestDeadline.Token;
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "";
            string method = context.Request.HttpMethod;
            if (method == "GET" && path == "/health")
            {
                await SendAsync(context.Response, 200, "text/plain; charset=utf-8", "OK", requestToken);
                return;
            }
            if (method == "GET" && path == "/export_stock_json")
            {
                await SendAsync(context.Response, 200, "application/json; charset=utf-8",
                    services.StockAsJson(), requestToken);
                return;
            }
            if (method == "POST" && path == "/batch")
            {
                long length = context.Request.ContentLength64;
                if (length < 0) {
                    await SendAsync(context.Response, 411, "text/plain", "Length Required", requestToken);
                    return;
                }
                if (length > MaxRequestBytes) {
                    // Permite vaciar cuerpos marginalmente mayores para que el cliente
                    // pueda recibir 413 sin un TCP RST por escritura interrumpida.
                    // Se mantiene un limite estricto de memoria y de lectura.
                    if (length <= MaxRequestBytes + 8192)
                    {
                        byte[] discarded = new byte[8192];
                        long pending = length;
                        while (pending > 0)
                        {
                            int n = await context.Request.InputStream.ReadAsync(
                                discarded.AsMemory(0, (int)Math.Min(discarded.Length, pending)), requestToken);
                            if (n == 0) break;
                            pending -= n;
                        }
                    }
                    context.Response.KeepAlive = false;
                    await SendAsync(context.Response, 413, "text/plain", "Payload Too Large", requestToken);
                    return;
                }
                using MemoryStream input = new();
                byte[] block = new byte[8192];
                long left = length;
                while (left > 0)
                {
                    int n = await context.Request.InputStream.ReadAsync(
                        block.AsMemory(0, (int)Math.Min(block.Length, left)), requestToken);
                    if (n == 0) throw new EndOfStreamException("Peticion HTTP incompleta.");
                    await input.WriteAsync(block.AsMemory(0, n), requestToken);
                    left -= n;
                }
                string csv = new UTF8Encoding(false, true).GetString(input.ToArray());
                using StringReader reader = new(csv);
                using StringWriter writer = new();
                services.RunBatch(reader, writer);
                await SendAsync(context.Response, 200, "text/csv; charset=utf-8",
                    writer.ToString(), requestToken);
                return;
            }
            await SendAsync(context.Response, 404, "text/plain", "Not Found", requestToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation while reading/writing a client must not leak a response.
        }
        catch (Exception e) when (e is IOException or DecoderFallbackException or
            ArgumentException or HttpListenerException or ObjectDisposedException)
        {
            // A disconnected peer may fail during SendAsync. Retrying a 400 on
            // the already broken socket would propagate and kill the listener.
            // Only malformed requests with an intact stream deserve a 400.
            if (e is EndOfStreamException or DecoderFallbackException or ArgumentException)
            {
                try
                {
                    if (context.Response.OutputStream.CanWrite)
                        await SendAsync(context.Response, 400, "text/plain", "Bad Request", requestToken);
                }
                catch (Exception sendError) when (sendError is IOException or
                    HttpListenerException or ObjectDisposedException or OperationCanceledException)
                {
                    // The client has disconnected or the server was cancelled.
                }
            }
        }
        finally
        {
            try { context.Response.Close(); }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) { /* Client disconnected during response shutdown. */ }
        }
    }

    private static async Task SendAsync(HttpListenerResponse response, int status,
        string mime, string content, CancellationToken cancel)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        response.StatusCode = status;
        response.ContentType = mime;
        response.ContentLength64 = bytes.Length;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = "no-store";
        await response.OutputStream.WriteAsync(bytes, cancel);
    }
}
