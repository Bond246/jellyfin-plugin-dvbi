using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;

namespace Jellyfin.Plugin.DvbI.ServiceLists;

/// <summary>
/// A region with its position in the hierarchy.
/// </summary>
/// <param name="Region">The region.</param>
/// <param name="Parent">Parent region or null.</param>
/// <param name="Depth">0 for top level regions.</param>
internal sealed record RegionNode(Region Region, RegionNode? Parent, int Depth)
{
    /// <summary>
    /// Gets the region ids from this region up to the top level.
    /// </summary>
    /// <returns>Ids, most specific first.</returns>
    public IEnumerable<string> SelfAndAncestorIds()
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            yield return node.Region.Id;
        }
    }
}
