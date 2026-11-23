# DVB-I plugin for Jellyfin – design

Target platform: Jellyfin 12.4 (.NET 10). Specification references: ETSI TS 103 770 / DVB BlueBook A177
(service discovery and programme metadata), A184 (implementation guidelines), A185 (accessibility),
ETSI TS 102 323 (TV-Anytime).

## Goal

DVB-I as a native Live TV source in Jellyfin: channels, programme guide, playback, recordings. Developed
and tested with German public broadcasters' DVB-I services, but not tied to a particular provider.

## Decisions

| # | Topic | Decision |
|---|---|---|
| 1 | Integration | `ITunerHost` + `IListingsProvider` (like Jellyfin's M3U/HDHomeRun support). Recordings through Jellyfin's DVR. |
| 2 | Sources | Service list registry URLs **and** direct service list URLs. No built-in default. |
| 3 | Setup | Only on the plugin page. The plugin creates its tuners and guide provider in the Live TV configuration itself. |
| 4 | Several lists | Allowed; one Jellyfin tuner per enabled list. Duplicates across lists are not merged. |
| 5 | Region | Per list: a postcode pre-selects the region, which can be changed. The top level means the LCN table without target region. |
| 6 | Channels | Services with an internet stream (DASH/HLS); with TVHeadend additionally broadcast-only services that TVHeadend receives unencrypted. |
| 7 | DASH | Manifest preparation for ffmpeg behind an exchangeable interface (`IStreamPreparer`), so an own DASH client could replace it. |
| 8 | Clients | Playback through Jellyfin (remux, no transcoding for internet streams). Additionally a client API with the original DVB-I data for own TV clients / HbbTV. |
| 9 | Guide | TV-Anytime: times, title, episode title, synopsis, image, genre → Jellyfin categories, parental rating, HD/subtitle/audio description/sign language flags, series ids for series recordings. |
| 10 | Channel numbers | LCN from DVB-I plus an offset per list (default 0). Services without LCN follow the highest LCN of the list. |
| 11 | Instance selection | DVB-I priority (lower value = higher priority), switch "prefer HLS" (default off), fallback on errors. |
| 12 | Language | Configuration page in English and German (following the Jellyfin UI language). Code and logs in English. |

## Technical baseline

- Schema versions: v6 (2024 namespaces) and v8 (2026 namespaces). Parsing ignores namespaces and matches
  local element names, tolerating unknown elements (A177 4.6.3).
- XML from remote sources is parsed with DTD processing prohibited (no entity expansion, no external
  entities); metadata responses are limited to 32 MB.
- HTTP: `Cache-Control: max-age` and `If-Modified-Since` (A177 4.3.2); User-Agent per A177 4.6.2. Responses
  are cached in memory and on disk so a temporarily unreachable server does not empty the channel list.
- Refresh: with Jellyfin's "Refresh Guide" task and whenever the plugin configuration is saved. Scheduled
  tasks are not registered yet when hosted services start, so the refresh is queued once available.
- Logos: `RelatedMaterial` with `HowRelatedCS:2021:1001.2`.
- LCN table: exact region → parent regions → table without `TargetRegion`.
- Tuners: `TunerHostInfo.Type = "dvbi"`, `TunerHostInfo.Url` = id of the service list in the plugin
  configuration. Jellyfin's web UI cannot configure tuner or guide provider types of plugins (it only
  offers a display name), hence the setup on the plugin page.
- Guide requests: 12-hour windows starting at multiples of 3 hours (A177 6.5.2.1),
  `ContentGuideServiceRef` takes precedence over `UniqueIdentifier`, 4 parallel requests per channel,
  one retry on 5xx, 4xx windows are not repeated for 6 hours.
- Some broadcasters redirect HTTPS manifest URLs to HTTP CDN URLs (for HbbTV devices). .NET does not
  follow HTTPS→HTTP redirects; the plugin follows redirects itself and tries the HTTPS variant first.

## DASH preparation

ffmpeg's DASH demuxer (libavformat/dashdec.c) has three weaknesses with live `$Number$` templates:

1. Start segment = `(now − availabilityStartTime − suggestedPresentationDelay) / duration`. Without a
   `suggestedPresentationDelay` it requests the segment that is still being produced → 404.
2. It never waits for the live edge: segments beyond the computed maximum are requested anyway and
   skipped on 404. Reading faster than real time it runs ahead of the live edge until the stream dies.
3. It opens all representations; Jellyfin uses the first video stream (seen: 320×180 on one service).

Solution:
- `DashManifestProxy` serves the manifest at `/DvbI/Dash/{token}/manifest.mpd` (anonymous, because ffmpeg
  does not authenticate; random 128-bit token per open stream, only for URLs the plugin registered – no
  SSRF). `DashManifestRewriter` sets `suggestedPresentationDelay` to max(3 segments, 6 s) (capped at
  timeShiftBufferDepth/2; with presentationTimeOffset also `minBufferTime`, which ffmpeg then subtracts as
  a segment count), keeps only the best video representation (H.264 preferred, ≤ 1080 lines) and the best
  representation per audio/text set, makes the BaseURL absolute and removes alternative BaseURLs and
  `Location`.
- `ReadAtNativeFramerate` (ffmpeg `-re`) for DASH sources keeps ffmpeg at that distance.
- Validated with 10-minute soak tests on seven channels without a single stall.

## Media sources and Jellyfin's probe cache

Jellyfin caches the probe result of a live stream permanently, keyed by the *requested* media source
(open token). Therefore:
- every delivery path of a channel is its own media source with its own id ("DVB-T2 (TVHeadend)",
  "Internet (DASH)", …); the id hashes everything that changes the stream (URL or TVHeadend service, DASH
  preparation, probe settings, cache version);
- when the plugin has to fall back to another path (e.g. no free tuner), probing is skipped for that open
  (`SupportsProbing = false`) instead of storing a probe of the wrong path under the requested id
  (otherwise an HEVC broadcast source got an H.264 probe and was remuxed with an H.264 bitstream filter).

Jellyfin's web playback info does not show the media source name; the path is recognisable by container
and codecs (`ts`/HEVC/AAC_LATM = broadcast via TVHeadend, `dash` or `hls`/H.264 = internet).

## Client API

`/DvbI/Channels`, `/{id}`, `/{id}/Schedule`, `/{id}/MoreEpisodes` for every signed-in user; see
[API.md](API.md). Original stream URLs of all instances, broadcast parameters, AIT URLs, the DVB-I schedule
with on-demand offers, More Episodes with pagination (links are only followed to the endpoint's server).

## Recordings

- Jellyfin's DVR records through the same chain as playback (`EncodedRecorder`: ffmpeg with `-re` for
  DASH, stream copy; TVHeadend sources use the `ts` container, so Jellyfin's `DirectRecorder` makes a 1:1
  copy including all audio tracks and teletext).
- Series recordings: guides seen so far carry no series ids, and some broadcasters put episode numbers
  into titles ("Morden im Norden (99)"). `SeriesTitle` splits "(n)", "(Folge n)" and "- Folge n" off the
  title (numbers 1900–2099 in brackets are treated as years) → title without number as name, number as
  `EpisodeNumber`, `SeriesId` from TV-Anytime `EpisodeOf` or the normalised title (channel independent),
  `ShowId` = CRID. Movies get no series id.
- Repeats are not flagged in the guide data → "record new episodes only" has no effect. CRIDs appear to
  be per broadcast, so duplicate detection across repeats is limited.

## TVHeadend hybrid

- Own connection settings in the plugin, with "take over from TVHeadend plugin" (reads its
  `TVHeadEnd.xml`). The TVHeadend plugin is not needed for DVB-I channels.
- "Prefer broadcast" (default off): broadcast instances received by TVHeadend join the selection; the DVB-I
  priority decides. If TVHeadend cannot deliver (not received, no free tuner), the internet stream follows.
- Broadcast-only services become channels when TVHeadend receives them; encrypted services only with
  "use encrypted services" (default off – TVHeadend reports encryption, not whether it can descramble).
- Matching by triplet (onid + sid; tsid only when both sides know it – DVB-I lists often omit it,
  TVHeadend reports 65536 for unknown) **plus a word-level name check**. On mismatch the internet stream is
  used and the conflict is logged and shown on the settings page (seen during development: ZDFinfo and
  zdf_neo with swapped DVB-T service ids in a service list).
- TVHeadend API (tested 4.3, API 20): `api/mpegts/mux/grid`, `api/mpegts/service/grid`, `api/serverinfo`.
- Own Digest handler (RFC 7616, MD5): TVHeadend rejects .NET's built-in Digest implementation.
- Streams via channel tickets (`playlist/ticket/channel/{uuid}`), so no credentials appear in the ffmpeg
  command lines Jellyfin logs (tickets are short-lived). Services without a TVHeadend channel cannot be used.
  Credentials embedded in the TVHeadend URL are moved into the credential fields.
- Before opening, the plugin checks that the stream starts (TVHeadend closes the connection without a free
  tuner).
- `AnalyzeDurationMs` 6000 for broadcast sources (HEVC streams joined between key frames).
- German DVB-T2: HEVC 1080p50 + AAC-LATM – higher resolution than the 720p internet streams, but clients
  without HEVC/LATM support get a transcoded stream. Hardware acceleration is configured per Jellyfin
  installation; the plugin creates its tuners with `AllowHWTranscoding = true`.

## Security notes

- Admin-only endpoints (`/DvbI/Registry`, `/DvbI/ServiceList…`, `/DvbI/Tvheadend…`) fetch URLs entered by
  administrators.
- The client API is available to every signed-in user; it exposes public stream and metadata URLs only.
- The only anonymous endpoint is the DASH manifest proxy (random per-stream token, idle timeout).
- The TVHeadend password is stored encrypted (`SecretProtector`: AES-256-GCM, random key in
  `secret.key` in the plugin data folder) and only ever leaves the server encrypted – the configuration page
  shows "stored" and keeps the value unless a new password is typed; "take over from TVHeadend plugin" also
  returns it encrypted. Plain text passwords of older configurations are encrypted at start-up. This
  protects configuration files, backups and the UI, not against full file access to the server.

## Observations from German DVB-I data (2026)

- Many services are available as DASH; a few also as HLS; the rest broadcast-only.
- Regions in up to four levels with regional LCN tables.
- Private broadcasters are signalled over IP only as HbbTV applications (`RelatedMaterial` with
  `LinkedApplicationCS:2019:1.x`, XML AIT), partly with PlayReady DRM.
- Guide data contains `OnDemandProgram` entries with HbbTV AIT deep links into broadcasters' media
  libraries; some content guide servers offer `MoreEpisodesEndpoint` (paginated).
- jellyfin-web only contains hls.js, no DASH player.

## Outlook

- **HbbTV**: an HbbTV integration (separate application) can use the AIT URLs from the client API.
- **Media libraries**: resolve the HbbTV deep links of on-demand offers to the web versions of the
  broadcasters' media libraries.
