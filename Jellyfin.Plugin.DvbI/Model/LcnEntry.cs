using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// One LCN assignment (clause 5.5.10).
/// </summary>
/// <param name="ChannelNumber">Logical channel number.</param>
/// <param name="ServiceRef">UniqueIdentifier of the service.</param>
/// <param name="Visible">Whether the service should be presented in normal navigation.</param>
internal sealed record LcnEntry(int ChannelNumber, string ServiceRef, bool Visible);
