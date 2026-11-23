using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.DvbI.Tvheadend;

/// <summary>
/// Answers HTTP Digest (RFC 7616, MD5, qop=auth) and Basic challenges. .NET's built-in Digest support is
/// rejected by TVHeadend 4.3, so the challenge is answered here: on 401 the request is repeated once with
/// an Authorization header.
/// </summary>
internal sealed partial class DigestAuthHandler : DelegatingHandler
{
    private readonly string _username;
    private readonly string _password;
    private int _nonceCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="DigestAuthHandler"/> class.
    /// </summary>
    /// <param name="username">User name.</param>
    /// <param name="password">Password.</param>
    /// <param name="inner">Inner handler.</param>
    public DigestAuthHandler(string username, string password, HttpMessageHandler inner)
        : base(inner)
    {
        _username = username;
        _password = password;
    }

    /// <summary>
    /// Builds the Authorization header value for a challenge.
    /// </summary>
    /// <param name="challenge">WWW-Authenticate header.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="uri">Request URI (path and query).</param>
    /// <param name="username">User name.</param>
    /// <param name="password">Password.</param>
    /// <param name="nonceCount">Nonce count.</param>
    /// <param name="cnonce">Client nonce.</param>
    /// <returns>The header value, or null for unsupported schemes.</returns>
    internal static string? Authorization(AuthenticationHeaderValue challenge, string method, string uri, string username, string password, int nonceCount, string cnonce)
    {
        if (string.Equals(challenge.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
        {
            return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
        }

        if (!string.Equals(challenge.Scheme, "Digest", StringComparison.OrdinalIgnoreCase) || challenge.Parameter is null)
        {
            return null;
        }

        var p = Parameters(challenge.Parameter);
        if (!p.TryGetValue("realm", out var realm) || !p.TryGetValue("nonce", out var nonce))
        {
            return null;
        }

        var algorithm = p.GetValueOrDefault("algorithm") ?? "MD5";
        if (!algorithm.Equals("MD5", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var qop = p.GetValueOrDefault("qop")?.Split(',').Select(q => q.Trim()).FirstOrDefault(q => q.Equals("auth", StringComparison.OrdinalIgnoreCase));
        var ha1 = Md5(username + ":" + realm + ":" + password);
        var ha2 = Md5(method + ":" + uri);
        var nc = nonceCount.ToString("x8", CultureInfo.InvariantCulture);
        var response = qop is null
            ? Md5(ha1 + ":" + nonce + ":" + ha2)
            : Md5(ha1 + ":" + nonce + ":" + nc + ":" + cnonce + ":" + qop + ":" + ha2);

        var header = new StringBuilder("Digest ");
        header.Append(CultureInfo.InvariantCulture, $"username=\"{username}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{uri}\", response=\"{response}\"");
        if (qop is not null)
        {
            header.Append(CultureInfo.InvariantCulture, $", qop={qop}, nc={nc}, cnonce=\"{cnonce}\"");
        }

        if (p.TryGetValue("opaque", out var opaque))
        {
            header.Append(CultureInfo.InvariantCulture, $", opaque=\"{opaque}\"");
        }

        return header.ToString();
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || request.Headers.Authorization is not null || request.RequestUri is null)
        {
            return response;
        }

        var challenge = response.Headers.WwwAuthenticate.FirstOrDefault(c => c.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase))
            ?? response.Headers.WwwAuthenticate.FirstOrDefault();
        var value = challenge is null
            ? null
            : Authorization(challenge, request.Method.Method, request.RequestUri.PathAndQuery, _username, _password, Interlocked.Increment(ref _nonceCount), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));
        if (value is null)
        {
            return response;
        }

        response.Dispose();
        using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        retry.Headers.TryAddWithoutValidation("Authorization", value);
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is mandated by HTTP Digest authentication (RFC 7616) as used by TVHeadend.")]
    private static string Md5(string value) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static Dictionary<string, string> Parameters(string parameter)
        => DigestParameter().Matches(parameter)
            .ToDictionary(m => m.Groups["key"].Value.ToLowerInvariant(), m => m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["plain"].Value, StringComparer.Ordinal);

    [GeneratedRegex("(?<key>[A-Za-z]+)=(?:\"(?<quoted>[^\"]*)\"|(?<plain>[^,\\s]+))", RegexOptions.CultureInvariant)]
    private static partial Regex DigestParameter();
}
