using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GooglePhotoWallpaper.Services.Google;

/// <summary>
/// Minimal loopback HTTP endpoint that catches the single OAuth redirect.
///
/// Deliberately a raw TcpListener rather than HttpListener: HttpListener needs a urlacl
/// reservation or elevation on some Windows configurations, which would be a terrible first-run
/// experience for a redistributable app. We only ever need to read one request line.
/// </summary>
public sealed class LoopbackListener : IDisposable
{
    private readonly TcpListener _listener;

    public LoopbackListener()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public string RedirectUri => $"http://127.0.0.1:{Port}/";

    /// <summary>Waits for the browser redirect and returns its query string parameters.</summary>
    public async Task<Dictionary<string, string>> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        using NetworkStream stream = client.GetStream();

        string requestLine = await ReadRequestLineAsync(stream, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> parameters = ParseQuery(requestLine);

        bool ok = parameters.ContainsKey("code");
        await WriteResponseAsync(stream, ok, parameters.GetValueOrDefault("error"), cancellationToken)
            .ConfigureAwait(false);

        return parameters;
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var builder = new StringBuilder();

        while (builder.Length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
            int newline = builder.ToString().IndexOf('\n');
            if (newline >= 0)
            {
                return builder.ToString()[..newline].Trim();
            }
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> ParseQuery(string requestLine)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // "GET /?code=xxx&state=yyy HTTP/1.1"
        string[] parts = requestLine.Split(' ');
        if (parts.Length < 2)
        {
            return result;
        }

        int queryStart = parts[1].IndexOf('?');
        if (queryStart < 0)
        {
            return result;
        }

        foreach (string pair in parts[1][(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string key = Uri.UnescapeDataString(pair[..eq].Replace('+', ' '));
            string value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream, bool success, string? error, CancellationToken cancellationToken)
    {
        string heading = success ? "연결되었습니다" : "연결에 실패했습니다";
        string detail = success
            ? "이 창을 닫고 Google Photo Wallpaper로 돌아가세요."
            : $"오류: {WebUtility.HtmlEncode(error ?? "unknown")}";
        string accent = success ? "#1a73e8" : "#d93025";

        string body = $"""
            <!doctype html>
            <html lang="ko"><head><meta charset="utf-8">
            <title>Google Photo Wallpaper</title></head>
            <body style="font-family:'Segoe UI','Malgun Gothic',sans-serif;background:#f8f9fa;
                         display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
              <div style="text-align:center;background:#fff;padding:48px 64px;border-radius:12px;
                          box-shadow:0 1px 3px rgba(0,0,0,.12)">
                <h1 style="color:{accent};font-size:22px;margin:0 0 12px">{heading}</h1>
                <p style="color:#5f6368;font-size:14px;margin:0">{detail}</p>
              </div>
            </body></html>
            """;

        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        byte[] header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _listener.Stop();
}
