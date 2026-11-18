using io.github.toyota32k.BooLib.Utils;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.toyota32k.BooLib.Server.client;

/**
 * REST API は、CustomHttpClient を利用するだけでSSL通信が可能だが、
 * MediaPlayer や Image(BitmapImage) など、通信に関与できないケースに対して、
 * localhost 上に中継サーバーを立て、Fingerprint 検証付きの ICustomHttpClient で
 * 実サーバーへ転送する。
 */
public sealed class LoopbackProxy : IDisposable {
    public static UtLog Logger = new UtLog("LoopbackProxy");

    private readonly object _lock = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _port;

    private ICustomPeer? _peer;
    private ICustomHttpClient? _client;

    /**
     * 中継先のホストを設定する。
     */
    public void SetPeer(ICustomPeer peer, HttpClient innerClient) {
        lock (_lock) {
            _client?.Dispose();
            _peer = peer;
            _client = new CustomHttpClient(peer, innerClient) {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }
    }

    public void SetPeer(ICustomPeer peer, ICustomHttpClient innerClient) {
        lock (_lock) {
            _client?.Dispose();
            _peer = peer;
            _client = innerClient;
            innerClient.Timeout = Timeout.InfiniteTimeSpan;
        }
    }

    /**
     * 中継を停止する。
     */
    public void ResetPeer() {
        lock (_lock) {
            _client?.Dispose();
            _client = null;
            _peer = null;
        }
    }

    /**
     * 実サーバーの URL を、中継サーバー経由の URL に変換する。
     * このラップされたURLを、ImageやMediaPlayerなどのSourceにセットする。
     */
    public string Wrap(string url) {
        EnsureStarted();
        return $"http://localhost:{_port}/?u={Uri.EscapeDataString(url)}";
    }

    private void EnsureStarted() {
        lock (_lock) {
            if (_listener != null) return;
            _port = FindFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{_port}/");
            listener.Start();
            _listener = listener;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => LoopAsync(listener, token));
        }
    }

    private async Task LoopAsync(HttpListener listener, CancellationToken token) {
        while (!token.IsCancellationRequested) {
            HttpListenerContext ctx;
            try {
                ctx = await listener.GetContextAsync();
            }
            catch {
                break;
            }
            _ = Task.Run(() => HandleAsync(ctx, token));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken token) {
        var response = ctx.Response;
        try {
            var target = ctx.Request.QueryString["u"];
            ICustomPeer? peer;
            ICustomHttpClient? client;
            lock (_lock) {
                peer = _peer;
                client = _client;
            }
            if (string.IsNullOrEmpty(target) || peer == null || client == null) {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Close();
                return;
            }
            // 現在接続中のホスト以外への中継は許可しない
            if (!peer.CheckUrl(target)) {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                response.Close();
                return;
            }

            var method = ctx.Request.HttpMethod == "HEAD" ? HttpMethod.Head : HttpMethod.Get;
            using var req = new HttpRequestMessage(method, target);
            var range = ctx.Request.Headers["Range"];
            if (!string.IsNullOrEmpty(range)) {
                req.Headers.TryAddWithoutValidation("Range", range);
            }

            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
            response.StatusCode = (int)res.StatusCode;
            var headers = res.Content.Headers;
            if (headers.ContentType != null) {
                response.ContentType = headers.ContentType.ToString();
            }
            if (headers.ContentLength is long len) {
                response.ContentLength64 = len;
            }
            else {
                response.SendChunked = true;
            }
            if (headers.ContentRange != null) {
                response.Headers["Content-Range"] = headers.ContentRange.ToString();
            }
            response.Headers["Accept-Ranges"] = "bytes";

            if (method != HttpMethod.Head) {
                await using var src = await res.Content.ReadAsStreamAsync(token);
                await src.CopyToAsync(response.OutputStream, token);
            }
            response.Close();
        }
        catch (HttpIOException ex) {
            // 上流（実サーバー）側でレスポンスが途中終了した
            Logger.Error(ex, $"upstream ended prematurely: {ex.HttpRequestError}");
            try { response.Abort(); } catch { }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or OperationCanceledException) {
            // シーク等によるクライアント側の切断は正常系
            try { response.Abort(); } catch { }
        }
        catch (Exception ex) {
            Logger.Error(ex, "proxy error");
            try { response.Abort(); } catch { }
        }
    }

    private static int FindFreePort() {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose() {
        lock (_lock) {
            _cts?.Cancel();
            _listener?.Close();
            _listener = null;
            _client?.Dispose();
            _client = null;
            _peer = null;
        }
    }
}
