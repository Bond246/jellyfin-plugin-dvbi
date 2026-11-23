# AGENTS.md – DVB-I plugin for Jellyfin

Jellyfin 12.4 plugin (.NET 10) that turns DVB-I service lists into a Live TV tuner and TV-Anytime data into
a programme guide, with optional broadcast reception through TVHeadend.

## Working agreements

- Code, comments, log messages, docs and commit messages in **English**. The configuration page is
  bilingual (English/German).
- Decisions live in [docs/DESIGN.md](docs/DESIGN.md). Ask before deviating; record new decisions there.
- Never commit DVB-I service list URLs that are not public, specifications (PDF), XML schemas, API keys,
  passwords or details of a local environment (host names, IP addresses, paths) – use generic
  placeholders such as `tvheadend.local`.
- Secrets in the configuration go through `SecretProtector` and are never sent to the page in plain text.

## Commands

```bash
dotnet build                 # analyzer warnings are errors (StyleCop, CA AllEnabledByDefault)
dotnet test                  # xUnit, Jellyfin.Plugin.DvbI.Tests
JELLYFIN_PLUGIN_DIR=… ./deploy.sh [Debug|Release]  # publish → plugin folder, meta.json, docker restart
```

Releases: set the four-part plugin version `X.Y.Z.0` in `build.yaml` and `Directory.Build.props` (Jellyfin
requests plugin images by the four-part version), push a tag `vX.Y.Z`; `.github/workflows/release.yml` builds,
tests, publishes the zip as GitHub release asset and updates `manifest.json`.

## Layout (`Jellyfin.Plugin.DvbI/`)

| Folder | Content |
|---|---|
| `Parsing/` | Namespace-agnostic parsers (local element names, DTDs prohibited) for service lists, registries, TV-Anytime |
| `Model/` | Records of the DVB-I model |
| `ServiceLists/` | Loading/caching, regions, LCN tables, channel line-up, playable instances |
| `Http/` | DVB-I HTTP client (max-age, If-Modified-Since, A177 User-Agent, disk cache, size limit) |
| `LiveTv/` | `ITunerHost` "dvbi", live stream, `TunerSyncService` (tuners + guide provider from the plugin configuration) |
| `Listings/` | `IListingsProvider` "dvbi", mapping to Jellyfin programmes, series titles |
| `Streaming/` | DASH manifest preparation and proxy (`IStreamPreparer`) |
| `Tvheadend/` | TVHeadend API client, Digest handler, matching DVB-I triplets ↔ TVHeadend services |
| `Api/` | Admin endpoints of the configuration page, DASH proxy endpoint, `Api/Client/` = client API, `Api/Tvheadend/` |
| `Configuration/` | Plugin configuration and the configuration page (`configPage.html`) |

## Conventions

- One type per file (SA1402/SA1649); configuration classes use arrays (serializer compatibility).
- Read XML only through `XmlHelpers.ParseSafe` and the local-name helpers – schema versions change namespaces.
- New parsing/mapping logic needs unit tests with **synthetic** test data (`TestData/`), no files from
  broadcasters or list providers.
- Only fetch URLs that come from service lists or the administrator's configuration; anonymous or user
  endpoints must not fetch arbitrary URLs (see the DASH proxy token and the MoreEpisodes same-origin check).

## Pitfalls (already investigated)

- jellyfin-web cannot configure plugin tuner/guide types → setup only on the plugin page;
  `TunerHostInfo.Url` = `ServiceListSource.Id`.
- Scheduled tasks are not registered when hosted services start ("Refresh Guide" is queued later).
- HTTPS→HTTP redirects (HbbTV CDNs) are not followed by .NET → resolve redirects manually, prefer HTTPS.
- ffmpeg's DASH demuxer: no live edge distance, never waits for segments, opens all representations →
  manifest preparation + `ReadAtNativeFramerate` (details in DESIGN.md).
- Jellyfin caches live stream probes per requested media source id forever → one media source per delivery
  path, version marker in the id hash, no probing after a fallback.
- Content guide requests: start/end on a 3-hour grid, windows of 6 or 12 hours (A177 6.5.2.1).
- TVHeadend: .NET Digest is rejected → own `DigestAuthHandler`; streams only via channel tickets (no
  credentials in URLs/logs); DVB-I lists usually omit tsid, TVHeadend reports 65536 for unknown ids;
  TVHeadend's own recordings occupy tuners.
- Specification texts can be made searchable by extracting the PDFs (e.g. with pypdf) into `docs/txt/`
  (ignored by git).
