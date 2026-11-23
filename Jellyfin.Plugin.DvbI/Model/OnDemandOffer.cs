using System;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// On-demand availability of a programme (TV-Anytime OnDemandProgram, TS 103 770 clause 6.10.8).
/// In German DVB-I guides the URLs point to XML AITs that start the broadcaster's HbbTV media library
/// with a deep link to the programme; they are not media URLs.
/// </summary>
/// <param name="ProgramUrl">ProgramURL, e.g. an XML AIT with a deep link.</param>
/// <param name="ContentType">ProgramURL@contentType.</param>
internal sealed record OnDemandOffer(string ProgramUrl, string? ContentType)
{
    /// <summary>Gets the AuxiliaryURL (e.g. AIT template without programme reference).</summary>
    public string? AuxiliaryUrl { get; init; }

    /// <summary>Gets the start of availability.</summary>
    public DateTime? AvailableFromUtc { get; init; }

    /// <summary>Gets the end of availability.</summary>
    public DateTime? AvailableUntilUtc { get; init; }

    /// <summary>Gets whether the offer is free of charge, null if not signalled.</summary>
    public bool? IsFree { get; init; }
}
