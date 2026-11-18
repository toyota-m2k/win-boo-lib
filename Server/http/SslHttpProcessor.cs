using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace io.github.toyota32k.BooLib.Server.http;

/// <summary>
/// SslStream で包んだ TLS サーバ用 Processor。
/// AuthenticateAsServer はコネクションごとに 1 回だけ実行する必要があるため、
/// HttpProcessor.WrapStream を override して、入出力で同じ SslStream インスタンスを使い回す。
/// </summary>
public class SslHttpProcessor : HttpProcessor {
    private readonly X509Certificate2 _cert;

    public SslHttpProcessor(X509Certificate2 cert) {
        _cert = cert;
    }

    protected override Stream WrapStream(TcpClient tcpClient) {
        tcpClient.ReceiveTimeout = 30000;
        // 動画ストリーミング中、クライアント(MediaPlayer)がバッファ満杯で受信を止めると
        // 同期 Write がブロックされるため、送信タイムアウトは無効化する（HTTP 時と同じ挙動）。
        tcpClient.SendTimeout = 0;

        var ssl = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
        // Tls13 は .NET 9 / Win11 では使用可能。Tls12 only な OS でも自動フォールバックする。
        ssl.AuthenticateAsServer(
            _cert,
            clientCertificateRequired: false,
            enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
            checkCertificateRevocation: false);
        return ssl;
    }
}
