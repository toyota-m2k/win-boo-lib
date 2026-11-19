using io.github.toyota32k.BooLib.Server.http.model;
using io.github.toyota32k.BooLib.Server.http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace io.github.toyota32k.BooLib.Server.http.response;

public interface IHttpResponse : IDisposable
{
    HttpRequest Request { get; }
    void WriteResponse(Stream outputStream);
}

public abstract class AbstractHttpResponse : IHttpResponse
{
    //private static Regex refererForCors = new Regex(@"(?<target>http://(?:localhost|127.0.0.1)(?::\d+)?)/");
    private static Regex refererForCors = new Regex(@"(?<target>https?://(?:.*)(?::\d+)?)/?");
    public HttpStatusCode StatusCode { get; set; }
    //public string ReasonPhrase { get; set; }
    public HttpRequest Request { get; }

    private string? getReferer() {
        if (Request.Headers.TryGetValue("origin", out var origin)) {
            return origin;
            //var m = refererForCors.Match(origin);
            //if (m.Success) {
            //    return m.Groups["target"]?.Value;
            //}
        }
        if (Request.Headers.TryGetValue("referer", out var referer)) {
            var m = refererForCors.Match(referer);
            if (m.Success) {
                return m.Groups["target"]?.Value;
            }
        }
        return null;
    }


    protected AbstractHttpResponse(HttpRequest req, HttpStatusCode statusCode)
    {
        Request = req;
        StatusCode = statusCode;
        // ローカルホストからの要求に対してはCross-Origin Resource Shareingを許可する
        var referer = getReferer();
        if (!string.IsNullOrEmpty(referer)) {
            Headers["Access-Control-Allow-Origin"] = referer;
            Headers["Access-Control-Allow-Methods"] = "GET,POST,PUT,DELETE";
            Headers["Access-Control-Allow-Headers"] = "Content-Type,Content-Length,Accept";
        }
    }

    public string GetHeaderValueOrDefault(string name, string def = "")
    {
        return Headers.TryGetValue(name, out var value) ? value : def;
    }

    public string ContentType
    {
        get => GetHeaderValueOrDefault("Content-Type");
        set => Headers["Content-Type"] = value;
    }
    public long ContentLength
    {
        get => Convert.ToInt64(GetHeaderValueOrDefault("Content-Length", "0"));
        set => Headers["Content-Length"] = $"{value}";
    }

    // Access-Control-Allow-Origin: https://127.0.0.1:3001
    // Access-Control-Max-Age:86400
    // Access-Control-Allow-Methods: GET,POST,PUT,PATCH,DELETE,HEAD,OPTIONS
    // Access-Control-Allow-Headers: Content-type,Accept,X-Custom-Header
    public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>()
    {
#if DEBUG
        //{ "Access-Control-Allow-Origin", "http://localhost:5501"},
        //{ "Access-Control-Allow-Methods", "GET,POST,PUT,DELETE"},
        //{ "Access-Control-Allow-Headers", "Content-Type,Content-Length,Accept" },
#endif
    };

    protected abstract void Prepare();

    /// <summary>
    /// 接続を維持するか (HttpProcessor がセットする)。
    /// 応答ボディの長さが確定しない場合や、Content-Length 通りに送信できなかった場合は false に戻される。
    /// </summary>
    public bool KeepAlive { get; set; } = false;

    private bool HasDefiniteBodyLength =>
        (int)StatusCode < 200
        || StatusCode == HttpStatusCode.NoContent
        || StatusCode == HttpStatusCode.NotModified
        || Headers.ContainsKey("Content-Length");

    protected virtual void WriteHeaders(Stream output)
    {
        if (KeepAlive && !HasDefiniteBodyLength) {
            // ボディの終端をクライアントが判断できないので、接続を閉じて終端を示す。
            KeepAlive = false;
        }
        if (KeepAlive) {
            Headers["Connection"] = "keep-alive";
            var version = Request.ProtocolVersion == "HTTP/1.1" ? "HTTP/1.1" : "HTTP/1.0";
            WriteText(output, $"{version} {(int)StatusCode} {StatusCode}\r\n");
        } else {
            // 1接続1リクエストで切断するため、クライアントに接続を再利用させない
            Headers["Connection"] = "close";
            WriteText(output, $"HTTP/1.0 {(int)StatusCode} {StatusCode}\r\n");
        }
        WriteText(output, string.Join("\r\n", Headers.Select(x => $"{x.Key}: {x.Value}")));
        WriteText(output, "\r\n");
    }
    protected abstract void WriteBody(Stream output);

    public virtual void WriteResponse(Stream output)
    {
        try {
            Prepare();
        } catch(HttpException ex) {
            ex.ErrorResponse.KeepAlive = KeepAlive;
            ex.ErrorResponse.WriteResponse(output);
            KeepAlive = ex.ErrorResponse.KeepAlive;
            return;
        }
        WriteHeaders(output);
        WriteText(output, "\r\n");
        if (KeepAlive && Request.Method != "HEAD" && Headers.ContainsKey("Content-Length")) {
            // 送信したボディ長が Content-Length と食い違うと後続のリクエストが壊れるので、その場合は接続を閉じる。
            var counter = new CountingWriteStream(output);
            WriteBody(counter);
            if (counter.Count != ContentLength) {
                KeepAlive = false;
            }
        } else {
            WriteBody(output);
        }
        output.Flush();
    }

    protected static void WriteText(Stream output, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }

    public virtual void Dispose() {
    }
}
