using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;

namespace Jellyfin.Plugin.DvbI.ServiceLists;

/// <summary>
/// A channel as presented to Jellyfin.
/// </summary>
/// <param name="Service">The DVB-I service.</param>
/// <param name="Number">Channel number including the list offset.</param>
/// <param name="Instances">Playable instances in preference order (IP, and broadcast via TVHeadend).</param>
internal sealed record LineupChannel(DvbIService Service, int Number, IReadOnlyList<PlayableInstance> Instances);
