namespace Jellyfin.Plugin.DvbI.Api.Tvheadend;

/// <summary>
/// TVHeadend connection settings as edited on the configuration page.
/// </summary>
/// <param name="Url">Base URL, e.g. http://tvheadend.local:9981.</param>
/// <param name="Username">User name.</param>
/// <param name="Password">Password.</param>
/// <param name="Profile">Streaming profile.</param>
/// <param name="AllowEncrypted">Whether encrypted services may be used.</param>
public sealed record TvheadendSettingsDto(string? Url, string? Username, string? Password, string? Profile, bool AllowEncrypted);
