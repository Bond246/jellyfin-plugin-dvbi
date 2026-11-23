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
/// A channel preview entry.
/// </summary>
/// <param name="Number">Channel number.</param>
/// <param name="Name">Name.</param>
/// <param name="Kind">Tv or Radio.</param>
/// <param name="Deliveries">Playable deliveries in the order they are tried.</param>
public sealed record ChannelDto(int Number, string Name, string Kind, IReadOnlyList<string> Deliveries);
