namespace Jellyfin.Plugin.DvbI.Api.Tvheadend;

/// <summary>
/// A DVB-I broadcast instance whose TVHeadend service cannot be used.
/// </summary>
/// <param name="Service">DVB-I service name.</param>
/// <param name="Delivery">DvbT, DvbS or DvbC.</param>
/// <param name="Triplet">DVB triplet onid.tsid.sid as signalled in DVB-I.</param>
/// <param name="TvheadendService">Name of the TVHeadend service with that triplet.</param>
/// <param name="Reason">NameMismatch, Encrypted or NoChannel.</param>
public sealed record TvheadendConflictDto(string Service, string Delivery, string Triplet, string? TvheadendService, string Reason);
