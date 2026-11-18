namespace io.github.toyota32k.BooLib.Server.client;

/**
 * CustomHttpClientやLoopbackProxyが証明書チェックを行うために必要な情報を供給するための i/f
 */
public interface ICustomPeer {
    string? Fingerprint { get; }

    /**
     * url は、この PeerHost が提供するサービスの URL かどうかをチェックする。
     */
    bool CheckUrl(string url);
}

