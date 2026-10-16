# Player site and "Copy player link": design

Date: 2026-10-15. Status: approved in chat, awaiting review of this written spec.

## Goal

Andrew wants a link he can type into Chrome that shows just the video, without the YouTube site
around it, plus a public web page where anyone can paste a YouTube link and watch it the same way.
If the video is later deleted or made private and re-uploaded under a new link, the page should
find the re-upload by itself and carry on from the exact second, switching over seamlessly.

### Facts this design rests on (tested 2026-10-15 in a browser)

- `youtube.com/embed/ID` and `youtube-nocookie.com/embed/ID` typed into the address bar show
  "Video player configuration error, Error 153": YouTube now refuses embeds without a Referer.
  `watch_popup` redirects to the same page; `origin`/`widget_referrer` parameters don't help.
- The same embed inside a normal https page plays.
- `https://www.youtube.com/oembed?format=json&url=...` answers cross-origin (CORS allowed) with
  title, author name and author URL, and with an error status for a missing video.
- YouTube's channel feed and its `youtubei/v1/search` endpoint are blocked cross-origin, so a
  static site cannot search YouTube by itself. A server-side relay can (like `yt.bat` with curl).

## Parts

### 1. "Copy player link" in the apps

- Link format: `https://nitro70.github.io/YoutubeDownloader/?v=VIDEO_ID`, plus `&t=SECONDS` when
  the source link carried a start time (`t` or `start`; forms `90`, `90s`, `1m30s`, `1h2m3s`).
- Core: `PlayerLink.TryCreate(string? text, out string link)` in YouTubeDownloader.Core. Accepts
  anything `YouTubeInput.TryGetUrl` accepts as a link; the video id comes from YoutubeExplode's
  `VideoId.TryParse` (watch, youtu.be, shorts, embed, live). Returns false for search words or a
  non-video link.
- Desktop GUI: a "Copy player link" button beside "Copy link", shown and enabled under the same
  rule (the box holds a video link). Copies the link and logs it.
- Phones (iOS and Android MainView): a "Copy player link" button in the video info card.
- CLI: `-p, --player-link` prints the player link for the given link or for the first search
  result (like `--search`), and on Windows also copies it to the clipboard. Listed in `--help`.

### 2. The website (`site/`, GitHub Pages)

Plain HTML, CSS and JavaScript modules, no build step, published by a Pages workflow.

- `site/index.html`, `site/style.css`, `site/app.js` (page logic), `site/logic.js` (pure
  functions, unit-tested with `node --test`), `site/config.js` (relay URL).
- Layout: dark, one thin bar at the top with the paste box and a Play button, the player filling
  the rest of the window, a one-line status message. Works on phones.
- Paste: accepts the same link forms as the apps, or a bare 11-character id. Bad input gets a
  plain message. Playing sets the address to `?v=ID` (`history.replaceState`), so the page can be
  bookmarked and shared; `?v=` on load plays straight away.
- Player: YouTube IFrame Player API with host `https://www.youtube-nocookie.com`, `playsinline`,
  `rel=0`, `start` from the saved time or `t`.
- Remembered time: `localStorage` entry per video, key `ytp:v:ID`, value
  `{ id, title, channel, channelUrl, duration, time, updated }`. Written every 2 s while playing and
  on `pagehide`/`visibilitychange`. On load, an explicit `t` wins; otherwise the saved time is used
  unless it is within 5 s of the end. Title and channel come from oEmbed when the video first
  loads (so they are known before any removal).
- Removal check: while the page is visible and a video is loaded, oEmbed is asked every 60 s, and
  at once on a player error. A 400/401/403/404 for a video that oEmbed knew before means "gone".
  Network errors are ignored until the next check. Player error 101/150 (embedding disabled by
  the uploader) is not "gone": the page shows a "Watch on YouTube" link instead.
- Finding the re-upload: ask the relay for the remembered title. A result qualifies only if:
  - it is a different id;
  - same channel (handle from oEmbed's author URL equals the result's channel URL handle, or the
    channel names match exactly after normalizing);
  - title similarity at least 0.85 (lowercase, punctuation and emoji removed, word-set Dice
    coefficient);
  - duration within 3 s when both are known; when the old duration is unknown the title
    similarity must be at least 0.95.
  The best-scoring result wins. None: "This video was removed and no re-upload was found" plus the
  paste box.
- Switching over:
  - If the old player is still playing: create a second player for the new id in a second
    container behind the first, muted, starting at the old player's current time. When it reaches
    PLAYING, seek it to the old player's time again (to absorb drift), then over 1 s fade the old
    out (opacity and volume down) and the new in (opacity and volume up to the old volume, unmuted
    if the old was). Then destroy the old player, delete its `localStorage` entry, store the new
    one with the same time, and set the address to the new `?v=`.
  - If the old player is not playing (paused, ended, errored): load the new video at the saved
    time, keep it paused if the old was paused, delete the old entry, update the address.
  - Status line: "Video was replaced, continuing with the new upload."
- Test hook, only on `localhost`/`127.0.0.1`: `?debugReplaceWith=ID&debugAfter=SECONDS` marks the
  video gone after that many seconds and uses ID as the found re-upload (skipping the relay), so
  the switch can be tested without a real deletion.

### 3. The search relay (`relay/`, Cloudflare Worker "ytd-relay")

- `GET /search?q=TEXT` (q 1 to 200 characters): POSTs to
  `https://www.youtube.com/youtubei/v1/search?prettyPrint=false` with the WEB client context used
  by `yt.bat`, and returns up to 10 plain videos as
  `[{ id, title, channel, channelId, channelUrl, lengthSeconds }]`.
- CORS: answers only requests whose `Origin` is `https://nitro70.github.io` or `http://localhost`
  / `http://127.0.0.1` on any port; anything else gets 403. `OPTIONS` preflight handled.
- Cache: responses kept 10 minutes in the Worker cache.
- Errors: YouTube failure gives 502 with a short JSON message; bad input 400.
- Deployed with `npx wrangler deploy` from `relay/` on Andrew's account (already signed in). Its
  `*.workers.dev` address goes into `site/config.js`.

### 4. Publishing

- GitHub Pages switched on for the repo with the "GitHub Actions" source (`gh api`), and
  `.github/workflows/pages.yml` uploads `site/` and deploys it on pushes to `main` that touch
  `site/**` (and on demand).
- The apps ship as 4.1.0 through the existing release workflow.

## Testing

- `site/logic.test.mjs` with `node --test`: id parsing for every link form, start time parsing,
  title normalizing and similarity, duration parsing, and `pickReplacement` against real relay
  output saved as a fixture (accepts the right match, rejects other channels, other titles,
  different durations, the same id).
- Relay: run with `wrangler dev` and the deployed one with curl: a real search returns plain
  videos with lengths; a wrong `Origin` gets 403; empty or 201-character `q` gets 400.
- Site in the built-in browser from a local static server: paste a link and it plays; reload and
  the player starts at the saved second; `?t=` wins over the saved time; the test hook switches
  to another video with the time continuing (checked through the player API) and the old entry
  deleted; bad input and an embedding-disabled video show their messages. Then the same basic
  checks on the published `nitro70.github.io` site.
- Core: `PlayerLink.TryCreate` cases (all link forms, `t`/`start`, search words, playlists and
  channels rejected).
- Apps: desktop GUI and iPhone screen headless harnesses check the button and the copied link;
  CLI `--player-link` with a link and with search words.

## Known limits

- Videos whose uploader disabled embedding can't play on the site.
- YouTube may show ads inside the embedded player.
- If YouTube blocks searches from Cloudflare, finding re-uploads stops; the rest keeps working.
- A re-upload under a different title or on another channel won't be found (by design: avoids
  switching to the wrong video).

## Not included

Searching by words on the site, playlists, live streams, offline use.
