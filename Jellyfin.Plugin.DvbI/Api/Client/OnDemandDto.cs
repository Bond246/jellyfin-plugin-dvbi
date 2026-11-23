using System;

namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// On-demand availability of a programme. In German DVB-I guides the URL is an XML AIT that starts the
/// broadcaster's HbbTV media library with a deep link to the programme.
/// </summary>
/// <param name="ProgramUrl">ProgramURL, e.g. XML AIT.</param>
/// <param name="ContentType">Content type of the ProgramURL.</param>
/// <param name="AuxiliaryUrl">AuxiliaryURL, e.g. AIT template.</param>
/// <param name="AvailableFrom">Start of availability.</param>
/// <param name="AvailableUntil">End of availability.</param>
/// <param name="Free">Whether free of charge, null if not signalled.</param>
public sealed record OnDemandDto(string ProgramUrl, string? ContentType, string? AuxiliaryUrl, DateTime? AvailableFrom, DateTime? AvailableUntil, bool? Free);
