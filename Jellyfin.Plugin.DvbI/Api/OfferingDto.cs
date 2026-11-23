using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.ServiceLists;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DvbI.Api;

/// <summary>
/// A service list offered by a registry.
/// </summary>
/// <param name="Name">Name.</param>
/// <param name="Url">Service list URL.</param>
/// <param name="Provider">Provider.</param>
/// <param name="Deliveries">Offered delivery types.</param>
/// <param name="Countries">Target countries.</param>
public sealed record OfferingDto(string Name, string Url, string Provider, IReadOnlyList<string> Deliveries, IReadOnlyList<string> Countries);
