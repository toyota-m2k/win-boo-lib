// Copyright (C) 2016 by David Jeske, Barend Erasmus and donated to the public domain

using io.github.toyota32k.BooLib.Server.http.model;
using io.github.toyota32k.BooLib.Server.http.response;
using io.github.toyota32k.BooLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace io.github.toyota32k.BooLib.Server.http;

public class HttpProcessor {

    #region Fields

    //private static int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

    private List<Route> Routes = new List<Route>();
    private UtLog Logger = new UtLog(typeof(HttpProcessor));
    private readonly ConcurrentDictionary<int, TcpClient> _clients = new();

    /// <summary>true なら Keep-Alive (1接続で複数リクエスト) を有効にする。false なら従来通り Connection: close。</summary>
    public bool IsKeepAlive { get; set; } = true;
    /// <summary>Keep-Alive 時、次のリクエストを待つ最大時間(ms)</summary>
    public int KeepAliveTimeoutMs { get; set; } = 15000;
    /// <summary>Keep-Alive 時、1接続で処理する最大リクエスト数</summary>
    public int MaxKeepAliveRequests { get; set; } = 100;
    /// <summary>Keep-Alive 時、未読リクエストボディを読み捨てる上限。超えたら接続を閉じる。</summary>
    public long MaxDrainBytes { get; set; } = 1024 * 1024;

    #endregion

    #region Constructors

    public HttpProcessor() {
    }

    #endregion

    #region Public Methods
    public void HandleClient(int id, TcpClient tcpClient) {
        Task.Run(() => {
            using (tcpClient) {
                try {
                    // 終了時にクローズできるように、接続中の TcpClient を保持しておく。
                    _clients[id] = tcpClient;

                    // SSL 対応のため、入出力で同じ Stream を使う必要がある (SslStream は 1 コネクションで 1 インスタンス)。
                    Stream stream;
                    try {
                        stream = WrapStream(tcpClient);
                    }
                    catch (System.Security.Authentication.AuthenticationException authEx) {
                        // TLS ハンドシェイク失敗は HTTPS リスナー上では「想定内ノイズ」のことが多い:
                        //   - 素の HTTP クライアントが HTTPS ポートを叩いた (typo / port scanner)
                        //   - クライアント側で fingerprint pin 不一致 → 接続を即時切断
                        //   - 古い TLS バージョンのクライアント
                        // 本物のサーバ側不具合ではないので Info レベルに落とす。
                        string peer = tcpClient.Client.RemoteEndPoint?.ToString() ?? "?";
                        // HRESULT も出す。Win32Exception なら NativeErrorCode が SChannel SSPI のエラーコード
                        // (SEC_E_CERT_UNKNOWN=0x80090327, SEC_E_NO_CREDENTIALS=0x8009030E 等) になるので
                        // 不具合解析時に直接 SChannel 側へ当たれる。
                        var baseEx = authEx.GetBaseException();
                        int hr = baseEx is System.ComponentModel.Win32Exception w32 ? w32.NativeErrorCode : authEx.HResult;
                        Logger.Info($"[{id}] TLS handshake failed from {peer}: {baseEx.Message} (HRESULT=0x{hr:X8})");
                        return;
                    }
                    catch (System.IO.IOException ioEx) {
                        // 接続が途中で切れただけ。ログを汚さないよう Info で。
                        Logger.Info($"[{id}] Connection aborted before request: {ioEx.Message}");
                        return;
                    }
                    catch (Exception e) {
                        // 上記以外は本当に想定外。Error で残す。
                        Logger.Error(e, $"[{id}] WrapStream failed unexpectedly.");
                        return;
                    }
                    Logger.Debug($"[{id}] Request Accepted.");

                    string peerAddress = "";
                    string? address_port = tcpClient.Client.RemoteEndPoint?.ToString();
                    if (address_port != null && address_port.Contains(':')) {
                        peerAddress = address_port.Substring(0, address_port.IndexOf(":"));
                    }

                    var chronos = new Chronos(Logger);
                    string url = "?";
                    try {
                        int requestCount = 0;
                        while (true) {
                            int firstByte = -1;
                            if (requestCount > 0) {
                                // Keep-Alive: 次のリクエストを待つ。タイムアウトや切断は正常終了として扱う。
                                firstByte = WaitForNextRequest(tcpClient, stream);
                                if (firstByte < 0) {
                                    Logger.Debug($"[{id}] Keep-Alive connection closed (idle or by client).");
                                    break;
                                }
                            }
                            requestCount++;

                            bool keepAlive = false;
                            RequestBodyStream? body;
                            using (IHttpResponse response = ProcessRequest(id, peerAddress, stream, stream, chronos, firstByte, out body)) {
                                try {
                                    url = response.Request?.Url ?? "?";
                                    if (response is AbstractHttpResponse ar) {
                                        keepAlive = ShouldKeepAlive(response.Request, body, requestCount);
                                        ar.KeepAlive = keepAlive;
                                    }
                                    Logger.Info($"[{id}] Responding: {url}");
                                    response.WriteResponse(stream);
                                    stream.Flush();
                                    if (response is AbstractHttpResponse ar2) {
                                        keepAlive = keepAlive && ar2.KeepAlive;
                                    }
                                    Logger.Info($"[{id}] Succeeded: {url}");
                                }
                                catch (Exception e) {
                                    keepAlive = false;
                                    Logger.Error(e, $"[{id}] Failed: {url}");
                                }
                                finally {
                                    chronos.Lap($"[{id}] Completed: {url}");
                                }
                            }
                            // 未読のリクエストボディが残っていると次のリクエストとして解釈されてしまうので読み捨てる。
                            if (keepAlive && (body == null || !body.Drain(MaxDrainBytes))) {
                                keepAlive = false;
                            }
                            if (!keepAlive) {
                                break;
                            }
                        }
                    }
                    finally {
                        try { stream.Dispose(); } catch { /* ignore */ }
                    }
                    //Logger.Debug($"[{id}] Shutdown Send-Socket: {url}");
                    ////var lingerOption = new LingerOption(true, 60);
                    ////tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, lingerOption);
                    //tcpClient.Client.Shutdown(SocketShutdown.Send);

                    //// クライアントが接続を切るまで待機
                    //Logger.Debug($"[{id}] Finishing: {url} ...");
                    //if (IsSocketConnected(tcpClient.Client)) {
                    //    WaitForClosed(inputStream);
                    //}
                    Logger.Debug($"[{id}] Finished: {url}");
                }
                finally {
                    _clients.TryRemove(id, out _);
                }
            }
        });
    }

    public void CloseAllClients() {
        foreach (var c in _clients.Values) {
            try { c.Close(); } catch { }
        }
        _clients.Clear();
    }

    //private bool IsSocketConnected(Socket s) {
    //    bool part1 = s.Poll(1000, SelectMode.SelectRead);
    //    bool part2 = (s.Available == 0);
    //    if (part1 && part2) {
    //        Logger.Debug("Disconnected.");
    //        return false;
    //    }
    //    else {
    //        Logger.Debug("Connected.");
    //        return true;
    //    }
    //}

    //private void WaitForClosed(Stream inputStream) {
    //    try {
    //        byte[] buffer = new byte[1024];
    //        int byteCount;
    //        while ((byteCount = inputStream.Read(buffer, 0, buffer.Length)) > 0) {
    //            //何もしない
    //        }
    //        Logger.Debug("Closed.");
    //    } catch (Exception e) {
    //        Logger.Debug("Closed (with error).");
    //    }
    //}

    // this formats the HTTP response...

    public void AddRoute(Route route) {
        Routes.Add(route);
    }

    #endregion

    #region Private Methods

    private static string Readline(Stream stream, int firstByte = -1) {
        var sb = new StringBuilder();
        while (true) {
            int c;
            if (firstByte >= 0) {
                c = firstByte;
                firstByte = -1;
            } else {
                c = stream.ReadByte();
            }
            if (c == -1) {
                // 接続が閉じられた / タイムアウト到達。EOS を例外で抜けて呼び出し側の try/catch で扱う。
                throw new IOException("End of stream while reading line.");
            }
            if (c == '\n') break;
            if (c == '\r') continue;
            sb.Append((char)c);
        }
        return sb.ToString();
    }

    private static void Write(Stream stream, string text) {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Keep-Alive 接続で次のリクエストの先頭 1 バイトを待つ。
    /// リクエスト間の空行 (CRLF) は読み飛ばす。
    /// </summary>
    /// <returns>先頭バイト。切断/タイムアウトの場合は -1</returns>
    private int WaitForNextRequest(TcpClient tcpClient, Stream stream) {
        int orgTimeout = tcpClient.ReceiveTimeout;
        try {
            tcpClient.ReceiveTimeout = KeepAliveTimeoutMs;
            while (true) {
                int c = stream.ReadByte();
                if (c == '\r' || c == '\n') continue;
                return c;
            }
        }
        catch (IOException) {
            return -1;
        }
        catch (ObjectDisposedException) {
            return -1;
        }
        catch (SocketException) {
            return -1;
        }
        finally {
            try { tcpClient.ReceiveTimeout = orgTimeout; } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// このリクエストの後も接続を維持してよいか。
    /// </summary>
    private bool ShouldKeepAlive(HttpRequest? request, RequestBodyStream? body, int requestCount) {
        if (!IsKeepAlive || request == null || body == null) return false;
        if (requestCount >= MaxKeepAliveRequests) return false;
        var connection = GetValueOrNull(request.Headers, "connection", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (connection.Any(it => it.Equals("close", StringComparison.OrdinalIgnoreCase))) return false;
        if (request.ProtocolVersion == "HTTP/1.1") return true;
        return connection.Any(it => it.Equals("keep-alive", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// TcpClient から 1 コネクションぶんの読み書き Stream を取り出す。
    /// SSL 派生クラスはここを override して SslStream を返す。
    /// </summary>
    protected virtual Stream WrapStream(TcpClient tcpClient) {
        // ハンドシェイク中の slowloris 等を避けるための保険
        tcpClient.ReceiveTimeout = 30000;
        tcpClient.SendTimeout = 30000;
        return tcpClient.GetStream();
    }


    //private IHttpResponse RouteRequest(Stream inputStream, Stream outputStream, HttpRequest request) {

    //    List<Route> routes = Routes.Where(x => Regex.Match(request.Url, x.UrlRegex).Success).ToList();

    //    if (!routes.Any()) {
    //        return HttpErrorResponse.NotFound(request);
    //    }

    //    Route? route = routes.FirstOrDefault(x => x.Method == request.Method);

    //    if (route == null) {
    //        return HttpErrorResponse.MethodNotAllowed(request);
    //    }

    //    // extract the path if there is one
    //    var match = Regex.Match(request.Url, route.UrlRegex);
    //    if (match.Groups.Count > 1) {
    //        request.Path = match.Groups[1].Value;
    //    }
    //    else {
    //        request.Path = request.Url;
    //    }

    //    // trigger the route handler...
    //    request.Route = route;
    //    try {
    //        return route.Process(request);
    //    }
    //    catch (Exception ex) {
    //        Logger.LogError(ex, "Processing Route.");
    //        return HttpErrorResponse.InternalServerError(request);
    //    }

    //}

    private string GetValueOrNull(Dictionary<string, string> map, string key, string def = "") {
        return map.TryGetValue(key, out var value) ? value : def;
    }

    private bool IsTextType(string contentType) {
        var type = contentType.ToLower();
        if (type.StartsWith("text/")) return true;
        if (type.StartsWith("application/json")) return true;
        else return false;
    }

    private IHttpResponse ProcessRequest(int id, string peerAddress, Stream inputStream, Stream outputStream, Chronos chronos, int firstByte, out RequestBodyStream? body) {
        body = null;
        try {
            var request = ParseHeader(id, peerAddress, inputStream, outputStream, firstByte);
            chronos.Start($"[{id}] request: {request.Url}");
            if (IsKeepAlive && !request.Headers.ContainsKey("transfer-encoding")) {
                // Keep-Alive 時は Content-Length 分だけ読める Stream でボディを扱う。
                // (transfer-encoding 付きはボディ長が分からないので body=null → 接続を閉じる)
                long contentLength = Convert.ToInt64(GetValueOrNull(request.Headers, "content-length", "0"));
                body = new RequestBodyStream(inputStream, contentLength);
            }
            Route route = GetRoute(request);
            ParseContent(body ?? inputStream, request, route);
            return route.Process(request);
        }
        catch (Exception ex) {
            if (ex is HttpException httpException) {
                Logger.Error($"[{id}] Routing Error");
                httpException.ErrorResponse.WriteLog(Logger, "  ");
                return httpException.ErrorResponse;
            } else if (ex is HttpCorsException corsException) {
                return corsException.Response;
            }
            else {
                Logger.Error(ex, $"[{id}] Routing Error");
                return HttpErrorResponse.InternalServerError(HttpRequest.InvalidRequest(id));
            }
        }
    }


    private HttpRequest ParseHeader(int id, string peerAddress, Stream inputStream, Stream outputStream, int firstByte) {
        //Read Request Line
        string request = Readline(inputStream, firstByte);

        string[] tokens = request.Split(' ');
        if (tokens.Length != 3) {
            throw new Exception("invalid http request line");
        }
        string method = tokens[0].ToUpper();
        string url = tokens[1];
        string protocolVersion = tokens[2];

        //Read Headers
        Dictionary<string, string> headers = new Dictionary<string, string>();
        string line;
        while ((line = Readline(inputStream)) != null) {
            if (line.Equals("")) {
                break;
            }

            int separator = line.IndexOf(':');
            if (separator == -1) {
                throw new Exception("invalid http header line: " + line);
            }
            string name = line.Substring(0, separator);
            int pos = separator + 1;
            while (pos < line.Length && line[pos] == ' ') {
                pos++;
            }

            string value = line.Substring(pos, line.Length - pos);
            headers.Add(name.ToLower(), value);
        }
        return new HttpRequest(id, peerAddress, method, url, headers, outputStream, protocolVersion.ToUpper());
    }

    private Route GetRoute(HttpRequest request) {
        List<Route> routes = Routes.Where(x => Regex.Match(request.Url, x.UrlRegex).Success).ToList();

        if (!routes.Any()) {
            Logger.Error($"Not Found: {request.Method} {request.Url}");
            throw HttpErrorResponse.NotFound(request).Exception;
        }

        Route? route = routes.FirstOrDefault(x => x.Method == request.Method);

        if (route == null) {
            if(request.Method == "OPTIONS") {
                // CORS のための OPTIONS メソッドの場合は、例外を投げて上位で処理する
                Logger.Info("CORS request.");
                throw new HttpCorsException(request);
            }

            Logger.Error($"Not Found: {request.Method} {request.Url}");
            throw HttpErrorResponse.MethodNotAllowed(request).Exception;
        }

        // extract the path if there is one
        var match = Regex.Match(request.Url, route.UrlRegex);
        if (match.Groups.Count > 1) {
            request.Path = match.Groups[1].Value;
        }
        else {
            request.Path = request.Url;
        }
        return route;
    }



    private HttpRequest ParseContent(Stream inputStream, HttpRequest request, Route route) {
        model.HttpContent content;
        long contentLength = Convert.ToInt64(GetValueOrNull(request.Headers, "content-length", "0"));
        string contentType = GetValueOrNull(request.Headers, "content-type", "");
        //string contentEncoding = GetValueOrNull(request.Headers, "Content-Encoding", "");
        //string contentTransferEncoding = GetValueOrNull(request.Headers, "Content-Transfer-Encoding", "");

        if (route.ExpectTextBody && IsTextType(contentType) && 0 < contentLength) {
            int totalBytes = Convert.ToInt32(request.Headers["content-length"]);
            int bytesLeft = totalBytes;
            byte[] bytes = new byte[totalBytes];

            while (bytesLeft > 0) {
                int n = inputStream.Read(bytes, totalBytes - bytesLeft, bytesLeft);
                if (n <= 0) {
                    throw new IOException("End of stream while reading request body.");
                }
                bytesLeft -= n;
            }
            // エンコーディングは UTF8 限定
            content = new HttpContent(Encoding.UTF8.GetString(bytes), contentType, contentLength);
        }
        else {
            content = new HttpContent(inputStream, contentType, contentLength);
        }
        request.Content = content;
        return request;
    }

    #endregion
}
