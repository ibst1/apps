using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fermata;

/// <summary>
/// The link to the Fermata web page: a WebSocket on 127.0.0.1 only (nothing leaves the computer,
/// so no firewall question and no admin). Only the Fermata page may connect (Origin check),
/// so other web sites cannot see the slideshow.
/// The page says which mode is chosen ("role"); the helper sends "show-start"/"show-end" and,
/// in between, every slideshow frame as a binary JPEG message.
/// </summary>
sealed class PageServer
{
    public const int Port = 47811;
    static readonly string[] AllowedOrigins = { "https://apps.ibstedt.net" };

    public event Action Changed;
    readonly List<Client> _clients = new();
    TcpListener _listener;

    public sealed class Client
    {
        public WebSocket Ws;
        public string Role = "";
        public bool Rec;                    // the page says a recording is running
        public long RecMs;                  // how long it has run, as of RecAt
        public DateTime RecAt;
        public int Sending;                 // 1 while a frame is being sent (newer frames are skipped)
        public readonly SemaphoreSlim Gate = new(1, 1);
    }

    public IReadOnlyList<Client> Clients { get { lock (_clients) return _clients.ToList(); } }
    /// <summary>Mode chosen on the most recently connected page ("lecturer", "both", "tech" or "").</summary>
    public string Role { get { lock (_clients) return _clients.LastOrDefault()?.Role ?? ""; } }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, Port);
        _listener.Start();
        _ = AcceptLoop();
        Log.Write($"Listening on 127.0.0.1:{Port}");
    }

    async Task AcceptLoop()
    {
        while (true)
        {
            TcpClient tcp;
            try { tcp = await _listener.AcceptTcpClientAsync(); } catch { return; }
            _ = Handle(tcp);
        }
    }

    static bool OriginOk(string origin)
    {
        if (string.IsNullOrEmpty(origin)) return false;
        if (AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;
        // local development copy of the page
        return Uri.TryCreate(origin, UriKind.Absolute, out var u) && (u.Host == "localhost" || u.Host == "127.0.0.1");
    }

    async Task Handle(TcpClient tcp)
    {
        tcp.NoDelay = true;
        var stream = tcp.GetStream();
        Client c = null;
        try
        {
            // Read the HTTP upgrade request
            var buf = new byte[8192]; int n = 0;
            while (n < buf.Length)
            {
                int r = await stream.ReadAsync(buf.AsMemory(n));
                if (r == 0) return;
                n += r;
                if (Encoding.ASCII.GetString(buf, 0, n).Contains("\r\n\r\n")) break;
            }
            var req = Encoding.ASCII.GetString(buf, 0, n);
            var headers = req.Split("\r\n").Skip(1).Where(l => l.Contains(':'))
                .ToDictionary(l => l[..l.IndexOf(':')].Trim().ToLowerInvariant(), l => l[(l.IndexOf(':') + 1)..].Trim());
            headers.TryGetValue("origin", out var origin);
            if (!headers.TryGetValue("sec-websocket-key", out var key) || !OriginOk(origin))
            {
                Log.Write($"Refused connection from origin '{origin}'");
                var no = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n");
                await stream.WriteAsync(no); tcp.Close(); return;
            }
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var resp = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                                               $"Sec-WebSocket-Accept: {accept}\r\n\r\n");
            await stream.WriteAsync(resp);
            c = new Client { Ws = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(20)) };
            lock (_clients) _clients.Add(c);
            Log.Write($"Page connected ({origin})");
            await SendJson(c, new { type = "hello", app = "Fermata", version = Program.Version });
            Changed?.Invoke();

            var rbuf = new byte[16384];
            while (c.Ws.State == WebSocketState.Open)
            {
                var ms = new MemoryStream();
                WebSocketReceiveResult res;
                do
                {
                    res = await c.Ws.ReceiveAsync(rbuf, CancellationToken.None);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(rbuf, 0, res.Count);
                } while (!res.EndOfMessage);
                if (res.MessageType == WebSocketMessageType.Close) break;
                if (res.MessageType != WebSocketMessageType.Text) continue;
                try
                {
                    using var doc = JsonDocument.Parse(ms.ToArray());
                    var root = doc.RootElement;
                    if (root.TryGetProperty("role", out var role)) { c.Role = role.GetString() ?? ""; Log.Write($"Mode on page: {c.Role}"); Changed?.Invoke(); }
                    if (root.TryGetProperty("rec", out var recEl))
                    {
                        bool on = recEl.ValueKind == JsonValueKind.True;
                        if (on != c.Rec) Log.Write(on ? "Recording started (page)" : "Recording stopped (page)");
                        c.Rec = on; c.RecAt = DateTime.UtcNow;
                        c.RecMs = root.TryGetProperty("elapsed", out var el) && el.TryGetInt64(out var elapsedMs) ? elapsedMs : 0;
                    }
                }
                catch { }
            }
        }
        catch (Exception e) { Log.Write("client: " + e.Message); }
        finally
        {
            if (c != null) { lock (_clients) _clients.Remove(c); Log.Write("Page disconnected"); Changed?.Invoke(); }
            try { tcp.Close(); } catch { }
        }
    }

    public async Task SendJson(Client c, object o)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(o);
        await c.Gate.WaitAsync();
        try { if (c.Ws.State == WebSocketState.Open) await c.Ws.SendAsync(data, WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { }
        finally { c.Gate.Release(); }
    }

    public void Broadcast(object o) { foreach (var c in Clients) _ = SendJson(c, o); }

    /// <summary>Send a frame to the pages that want it; a page still busy with the previous frame skips this one.</summary>
    public void SendFrame(byte[] jpeg, Func<Client, bool> wants)
    {
        foreach (var c in Clients)
        {
            if (!wants(c) || Interlocked.CompareExchange(ref c.Sending, 1, 0) != 0) continue;
            _ = Task.Run(async () =>
            {
                await c.Gate.WaitAsync();
                try { if (c.Ws.State == WebSocketState.Open) await c.Ws.SendAsync(jpeg, WebSocketMessageType.Binary, true, CancellationToken.None); }
                catch { }
                finally { c.Gate.Release(); Interlocked.Exchange(ref c.Sending, 0); }
            });
        }
    }
}
