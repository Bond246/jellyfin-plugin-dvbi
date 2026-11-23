using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// An LCN table (clause 5.5.12).
/// </summary>
/// <param name="TargetRegions">Region ids this table applies to; empty means "anywhere".</param>
/// <param name="Entries">Channel number assignments.</param>
internal sealed record LcnTable(IReadOnlyList<string> TargetRegions, IReadOnlyList<LcnEntry> Entries);
