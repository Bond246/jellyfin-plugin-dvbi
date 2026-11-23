# DVB-I plugin – client API

For own TV clients and HbbTV integrations: the API exposes the original DVB-I data that Jellyfin itself
does not model (all service instances with stream URLs, broadcast parameters, HbbTV AITs, the DVB-I
schedule with on-demand offers, More Episodes).

- Base: `{jellyfin-server}/DvbI/Channels`
- Authentication: like any Jellyfin API (`Authorization: MediaBrowser Token="…"`), every signed-in user
- JSON in PascalCase; Jellyfin omits fields whose value is `null`
- Also described in Jellyfin's OpenAPI document (`/api-docs/swagger`)

## Endpoints

### `GET /DvbI/Channels`
All DVB-I channels, ordered by channel number.

```json
{
  "JellyfinId": "0123456789abcdef0123456789abcdef",
  "ChannelId": "dvbi_fedcba9876543210fedcba9876543210",
  "Number": "2", "Name": "ZDF HD", "Provider": "ZDF", "Kind": "Tv",
  "LogoUrl": "…", "ServiceId": "tag:zdf.de,2020:zdf",
  "ServiceListName": "My service list", "ServiceListUrl": "https://…",
  "Instances": [
    { "Priority": 1, "Delivery": "DvbS", "PlayableInJellyfin": false, "ContentProtection": false,
      "BroadcastParameters": { "DVBTriplet@origNetId": "1", "DVBTriplet@tsId": "1011",
                               "DVBTriplet@serviceId": "11110", "OrbitalPosition": "19.2", "Frequency": "1136200" },
      "LinkedApplications": [] },
    { "Priority": 3, "Delivery": "DvbT", "PlayableInJellyfin": true, "TvheadendService": "ZDF HD", … },
    { "Priority": 4, "Delivery": "Dash", "Url": "https://…/manifest.mpd", "PlayableInJellyfin": true, … },
    { "Priority": 5, "Delivery": "Hls",  "Url": "https://…/master.m3u8",  "PlayableInJellyfin": true, … }
  ],
  "LinkedApplications": [
    { "HowRelated": "urn:dvb:metadata:cs:LinkedApplicationCS:2019:1.1",
      "Url": "https://…/aitx.php?id=1", "ContentType": "application/vnd.dvb.ait+xml" }
  ],
  "Guide": { "ScheduleInfoEndpoint": "…", "ProgramInfoEndpoint": "…",
             "MoreEpisodesEndpoint": "…", "ServiceId": "tag:zdf.de,2020:zdf" }
}
```

- `Instances`: all instances in DVB-I priority order (lower value = higher priority), including broadcast
  and DRM-protected ones. `Delivery`: `Dash`, `Hls`, `DvbT`, `DvbS`, `DvbC`, `Other`. `Url` contains the
  **original URLs** of the service list (no redirect resolution, no DASH preparation for ffmpeg).
- `TvheadendService`: the TVHeadend service used for a broadcast instance, when "prefer broadcast" is on.
- `LinkedApplications`: HbbTV applications (XML AIT) at service or instance level; for some broadcasters
  the only IP access.
- `JellyfinId`: id of the Jellyfin Live TV channel (for `/LiveTv/…`, `/Items/{id}/PlaybackInfo`).

### `GET /DvbI/Channels/{id}`
One channel. `{id}` = `JellyfinId` or `ChannelId`.

### `GET /DvbI/Channels/{id}/Schedule?start=&end=`
Schedule directly from the DVB-I content guide server. `start` defaults to now, `end` to `start` + 12 h,
at most 7 days. Times in UTC (ISO 8601).

```json
{ "Crid": "crid://…", "Start": "…Z", "End": "…Z",
  "Title": "…", "EpisodeTitle": "…", "Synopsis": "…", "ImageUrl": "…",
  "GenreIds": ["1.1"], "MinimumAge": 12, "Hd": true,
  "Subtitles": true, "AudioDescription": false, "SignLanguage": false,
  "OnDemand": { "ProgramUrl": "https://…/vod.xait?…",
                "ContentType": "application/vnd.dvb.ait+xml", "AuxiliaryUrl": "…",
                "AvailableFrom": "…Z", "AvailableUntil": "…Z", "Free": true } }
```

- `GenreIds`: DVB ContentSubject terms (TS 103 770 annex D.5, same as the EIT content descriptor).
- `OnDemand.ProgramUrl`: for German public broadcasters an XML AIT that starts the HbbTV media library with
  a deep link to the programme.

### `GET /DvbI/Channels/{id}/MoreEpisodes?crid=&page=`
Further episodes of a programme (TS 103 770 clause 6.7), if the channel's content guide source has a
`MoreEpisodesEndpoint` (otherwise 404). `crid` comes from the schedule.

```json
{ "Programmes": [ { "Crid": "…", "Title": "…", "EpisodeTitle": "…", "OnDemand": { … } } ],
  "NextPage": "https://…/more/?pid=…&page=1" }
```

Pages hold up to 30 entries. For the next page pass `NextPage` **unmodified** as `page` (required by clause
6.9); links to other servers are rejected (404).

## Errors

| Status | Meaning |
|---|---|
| 401 | not signed in |
| 400 | invalid time range |
| 404 | unknown channel, no More Episodes endpoint or foreign page link |
| 502 | content guide server unreachable or invalid response |
