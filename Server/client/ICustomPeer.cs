using System;
using System.Collections.Generic;
using System.Text;

namespace io.github.toyota32k.BooLib.Server.client;

public interface ICustomPeer {
    string Fingerprint { get; }
    string BaseUrl { get; }
}
