using io.github.toyota32k.BooLib.Server.mdns;
using io.github.toyota32k.BooLib.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.toyota32k.BooLib.Server.client;

/**
 * ICustomHttpClientの実装クラス
 * innerClientでデータ送受信時に、Options に ICustomPeerを付与し、
 * CustomHttpClient.CreateHttpClientHandler() で Fingerprint 検証を行うスキームが構築できる。
 */
public class CustomHttpClient : ICustomHttpClient {
    public static UtLog Logger = new UtLog("PEHClient");
    private readonly ICustomPeer _peerHost;
    private readonly HttpClient _httpClient;

    public CustomHttpClient(ICustomPeer peerHost, HttpClient innerClient) {
        _peerHost = peerHost;
        _httpClient = innerClient;
    }

    public void Dispose() {
        _httpClient.Dispose();
    }

    // completionOption を下層の HttpClient まで伝える版。
    // ResponseHeadersRead を指定したいダウンロード系で必須（既定の ResponseContentRead だと
    // ボディを全部受信し終えるまで Task が完了せず、ストリーミング受信できない）。
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken? cancellationToken) {
        request.Options.Set(KEY, _peerHost);
        return _httpClient.SendAsync(request, completionOption, cancellationToken ?? CancellationToken.None);
    }
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken? cancellationToken) {
        return SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken ?? CancellationToken.None);
    }

    public Task<HttpResponseMessage> GetAsync([StringSyntax("Uri")] string requestUri, HttpCompletionOption completionOption, CancellationToken? cancellationToken) {
        var uri = new Uri(requestUri, UriKind.RelativeOrAbsolute);
        var req = new HttpRequestMessage(HttpMethod.Get, uri);
        return SendAsync(req, completionOption, cancellationToken);
    }
    public Task<HttpResponseMessage> GetAsync([StringSyntax("Uri")] string requestUri, CancellationToken? cancellationToken) {
        return GetAsync(requestUri, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    public Task<HttpResponseMessage> PostAsync([StringSyntax("Uri")] string requestUri, HttpContent? content, HttpCompletionOption completionOption, CancellationToken? cancellationToken) {
        var uri = new Uri(requestUri, UriKind.RelativeOrAbsolute);
        var req = new HttpRequestMessage(HttpMethod.Post, uri) {
            Content = content
        };
        return SendAsync(req, completionOption, cancellationToken);
    }
    public Task<HttpResponseMessage> PostAsync([StringSyntax("Uri")] string requestUri, HttpContent? content, CancellationToken? cancellationToken) {
        return PostAsync(requestUri, content, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    public Task<HttpResponseMessage> PutAsync([StringSyntax("Uri")] string requestUri, HttpContent? content, HttpCompletionOption completionOption, CancellationToken? cancellationToken) {
        var uri = new Uri(requestUri, UriKind.RelativeOrAbsolute);
        var req = new HttpRequestMessage(HttpMethod.Put, uri) {
            Content = content
        };
        return SendAsync(req, completionOption, cancellationToken);
    }

    public Task<HttpResponseMessage> PutAsync([StringSyntax("Uri")] string requestUri, HttpContent? content, CancellationToken? cancellationToken = null) {
        return PutAsync(requestUri, content, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }
    public TimeSpan Timeout {
        get => _httpClient.Timeout;
        set => _httpClient.Timeout = value;
    }
    //public static string HTTP_CLIENT_FACTORY_NAME = "CustomHttpClientFactory";
    public static readonly HttpRequestOptionsKey<ICustomPeer> KEY = new("PeerEndpoint");

    public ICustomHttpClient CreateHttpClient(HttpClient client) {
        return new CustomHttpClient(_peerHost, client);
    }

    /**
     * HttpClientBuilder の PrimaryHttpMessageHandler として設定するハンドラ
     * 
     * HTTPS 用 HttpClientHandler を生成。peer.IsHttps の場合、mDNS TXT で得た指紋と
     * 実際のサーバ証明書 SHA-256 を pin-to-fingerprint 比較する。
     * 
     * 使い方 (App.xaml.cs などで)：
     * ```
     * var services = new ServiceCollection();
     * services
     *     .AddHttpClient(HTTP_CLIENT_FACTORY_NAME)
     *     .ConfigurePrimaryHttpMessageHandler(() => CustomHttpClient.CreateHttpClientHandler());
     * ```
     */
    public static HttpClientHandler CreateHttpClientHandler() {
        return new HttpClientHandler {
            ServerCertificateCustomValidationCallback = (req, cert, chain, sslErrors) => {
                // HTTP は証明書検証不要
                if (req.RequestUri?.Scheme != Uri.UriSchemeHttps) return true;
                if (cert == null) {
                    Logger.Error("Sync: no certificate presented");
                    return false;
                }
                var peer = req.Options.TryGetValue(KEY, out ICustomPeer? v) ? v : null;
                if (peer == null) {
                    Logger.Error("Sync: no PeerHost info in request options");
                    return false;
                }
                var expected = peer.Fingerprint ?? "";
                string actual = CertificateGenerator.ComputeSha256Fingerprint(cert);
                bool ok = string.Equals(
                    PeerEndpoint.NormalizeFp(actual),
                    PeerEndpoint.NormalizeFp(expected),
                    StringComparison.OrdinalIgnoreCase);
                if (!ok) {
                    Logger.Error($"Sync: certificate fingerprint mismatch.\n  expected={expected}\n  actual  ={actual}");
                }
                return ok;
            }
        };
    }

}
