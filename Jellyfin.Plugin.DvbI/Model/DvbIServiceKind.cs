using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// Kind of service (from ServiceType).
/// </summary>
internal enum DvbIServiceKind
{
    /// <summary>Linear TV.</summary>
    Tv,

    /// <summary>Linear radio.</summary>
    Radio,

    /// <summary>Anything else.</summary>
    Other
}
