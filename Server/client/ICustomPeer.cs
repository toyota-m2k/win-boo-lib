using System;

namespace io.github.toyota32k.BooLib.Server.client;

public interface ICustomPeer {
    string Fingerprint { get; }

    /**
     * url は、この PeerHost が提供するサービスの URL かどうかをチェックする。
     */
    bool CheckUrl(string url);
}

