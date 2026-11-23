using System;
using Jellyfin.Plugin.DvbI.Configuration;

namespace Jellyfin.Plugin.DvbI.Tvheadend;

/// <summary>
/// TVHeadend connection settings.
/// </summary>
/// <param name="BaseUrl">Base URL ending with a slash, e.g. http://tvheadend.local:9981/.</param>
/// <param name="Username">User name.</param>
/// <param name="Password">Password.</param>
/// <param name="Profile">Streaming profile.</param>
internal sealed record TvhSettings(Uri BaseUrl, string Username, string Password, string Profile)
{
    /// <summary>
    /// Gets a value indicating whether a stored password could not be decrypted (e.g. the key file was lost);
    /// the password then has to be entered again.
    /// </summary>
    public bool PasswordUnreadable { get; init; }

    /// <summary>
    /// Gets a key identifying the connection (for caches).
    /// </summary>
    public string Key => BaseUrl + "|" + Username + "|" + Password.GetHashCode(StringComparison.Ordinal);

    /// <summary>
    /// Creates settings from the plugin configuration.
    /// </summary>
    /// <param name="config">Plugin configuration.</param>
    /// <returns>Settings, or null if TVHeadend is not configured.</returns>
    public static TvhSettings? From(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Create(config.TvheadendUrl, config.TvheadendUsername, config.TvheadendPassword, config.TvheadendProfile);
    }

    /// <summary>
    /// Creates settings from raw values.
    /// </summary>
    /// <param name="url">Base URL (scheme optional, default http).</param>
    /// <param name="username">User name.</param>
    /// <param name="password">Password, plain or encrypted by <see cref="SecretProtector"/>.</param>
    /// <param name="profile">Streaming profile.</param>
    /// <returns>Settings, or null if the URL is empty or invalid.</returns>
    public static TvhSettings? Create(string? url, string? username, string? password, string? profile)
    {
        var unreadable = false;
        if (SecretProtector.IsProtected(password))
        {
            password = SecretProtector.Default.Unprotect(password);
            unreadable = password is null;
        }

        var text = url?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text.TrimEnd('/') + "/", UriKind.Absolute, out var baseUrl)
            || baseUrl.Scheme is not ("http" or "https"))
        {
            return null;
        }

        // Credentials belong in the user name and password fields. Credentials embedded in the URL would end
        // up in logs and channel paths, so they are removed (and used if the fields are empty).
        if (!string.IsNullOrEmpty(baseUrl.UserInfo))
        {
            var parts = baseUrl.UserInfo.Split(':', 2);
            if (string.IsNullOrEmpty(username))
            {
                username = Uri.UnescapeDataString(parts[0]);
                password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : password;
            }

            baseUrl = new UriBuilder(baseUrl) { UserName = string.Empty, Password = string.Empty }.Uri;
        }

        return new TvhSettings(baseUrl, username ?? string.Empty, password ?? string.Empty, string.IsNullOrWhiteSpace(profile) ? "pass" : profile.Trim())
        {
            PasswordUnreadable = unreadable,
        };
    }
}
