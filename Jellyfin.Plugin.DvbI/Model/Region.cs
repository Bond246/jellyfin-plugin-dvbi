using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// A region of the RegionList, possibly nested.
/// </summary>
/// <param name="Id">Region@regionID.</param>
/// <param name="Name">First RegionName.</param>
/// <param name="Selectable">Region@selectable.</param>
/// <param name="Postcodes">Exact postcodes (normalised).</param>
/// <param name="WildcardPostcodes">Wildcard postcodes (normalised, one '*').</param>
/// <param name="PostcodeRanges">Postcode ranges (normalised).</param>
/// <param name="Children">Nested regions.</param>
internal sealed record Region(
    string Id,
    string Name,
    bool Selectable,
    IReadOnlyList<string> Postcodes,
    IReadOnlyList<string> WildcardPostcodes,
    IReadOnlyList<(string From, string To)> PostcodeRanges,
    IReadOnlyList<Region> Children);
