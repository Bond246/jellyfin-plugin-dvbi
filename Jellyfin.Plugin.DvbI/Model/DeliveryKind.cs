using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// How a service instance is delivered.
/// </summary>
internal enum DeliveryKind
{
    /// <summary>MPEG-DASH over IP.</summary>
    Dash,

    /// <summary>HLS over IP (OtherDeliveryParameters, vnd.apple.mpegurl).</summary>
    Hls,

    /// <summary>DVB-T broadcast.</summary>
    DvbT,

    /// <summary>DVB-S broadcast.</summary>
    DvbS,

    /// <summary>DVB-C broadcast.</summary>
    DvbC,

    /// <summary>Anything else (RTSP, multicast, unknown extension).</summary>
    Other
}
