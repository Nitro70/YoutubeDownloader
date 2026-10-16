// Pure logic for the player page: no DOM, no network. Unit-tested with `node --test site/`.

const ID = /^[A-Za-z0-9_-]{11}$/;

function toUrl(text) {
  const t = (text ?? '').trim();
  if (t === '') return null;
  try { return new URL(/^[a-z][a-z0-9+.-]*:\/\//i.test(t) ? t : 'https://' + t); } catch { return null; }
}

/** The video id in a YouTube link, or a bare 11-character id; null otherwise. */
export function parseVideoId(text) {
  const t = (text ?? '').trim();
  if (ID.test(t)) return t;
  const url = toUrl(t);
  if (!url) return null;
  const host = url.hostname.toLowerCase().replace(/^(www|m|music)\./, '');
  let id = null;
  if (host === 'youtu.be') id = url.pathname.split('/')[1];
  else if (host === 'youtube.com' || host === 'youtube-nocookie.com') {
    if (url.pathname === '/watch') id = url.searchParams.get('v');
    else id = /^\/(?:embed|shorts|live|v)\/([^/]+)/.exec(url.pathname)?.[1] ?? null;
  }
  return id && ID.test(id) ? id : null;
}

/** Seconds from "90", "90s", "1m30s", "1h2m3s"; null if not that shape. */
export function parseTime(value) {
  const v = String(value ?? '');
  if (/^\d+$/.test(v)) return Number(v);
  const m = /^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$/.exec(v);
  if (v === '' || !m) return null;
  return Number(m[1] ?? 0) * 3600 + Number(m[2] ?? 0) * 60 + Number(m[3] ?? 0);
}

/** Start seconds from a link's t= or start=; null when it has none. */
export function parseStart(text) {
  const url = toUrl(text);
  const v = url?.searchParams.get('t') ?? url?.searchParams.get('start');
  return v == null ? null : parseTime(v);
}

/** "4:05" or "1:02:03" to seconds; null otherwise. */
export function parseDuration(text) {
  if (typeof text !== 'string' || !/^\d+(:\d{1,2}){1,2}$/.test(text)) return null;
  return text.split(':').reduce((sum, part) => sum * 60 + Number(part), 0);
}

/** Lowercase words without accents, punctuation or emoji. */
export function normalizeTitle(s) {
  return String(s ?? '').toLowerCase().normalize('NFKD').replace(/[\u0300-\u036f]/g, '')
    .replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}

/** Dice coefficient of the two titles' word sets, 0 to 1. */
export function titleSimilarity(a, b) {
  const A = new Set(normalizeTitle(a).split(' ').filter(Boolean));
  const B = new Set(normalizeTitle(b).split(' ').filter(Boolean));
  if (A.size === 0 || B.size === 0) return 0;
  let common = 0;
  for (const w of A) if (B.has(w)) common++;
  return (2 * common) / (A.size + B.size);
}

/** "@handle" (lowercase) from ".../@handle" or "/@handle"; null otherwise. */
export function channelHandle(url) {
  const m = /\/(@[^/?#]+)/.exec(String(url ?? ''));
  return m ? decodeURIComponent(m[1]).toLowerCase() : null;
}

/**
 * The re-upload of `gone` among relay results, or null.
 * gone: { id, title, channel, channelUrl, duration }; results: relay output.
 */
export function pickReplacement(gone, results) {
  const handle = channelHandle(gone.channelUrl);
  const channelName = normalizeTitle(gone.channel);
  let best = null;
  let bestScore = 0;
  for (const r of results ?? []) {
    if (!r?.id || r.id === gone.id) continue;
    const sameChannel = (handle !== null && channelHandle(r.channelUrl) === handle)
      || (channelName !== '' && normalizeTitle(r.channel) === channelName);
    if (!sameChannel) continue;
    const score = titleSimilarity(gone.title, r.title);
    if (gone.duration > 0 && r.lengthSeconds > 0) {
      if (Math.abs(gone.duration - r.lengthSeconds) > 3 || score < 0.85) continue;
    } else if (score < 0.95) continue;
    if (score > bestScore) { best = r; bestScore = score; }
  }
  return best;
}

/** Where to start: an explicit t, else the saved time unless within 5 s of the end. */
export function resumeTime(explicitT, saved) {
  if (explicitT != null) return explicitT;
  if (!saved || !(saved.time > 0)) return 0;
  if (saved.duration > 0 && saved.time > saved.duration - 5) return 0;
  return saved.time;
}

/** oEmbed answers that mean the video is gone (not network trouble or rate limits). */
export function isGoneStatus(status) {
  return status === 400 || status === 401 || status === 403 || status === 404;
}

/** The title as a relay search, trimmed to the relay's 200-character limit. */
export function searchQuery(title) {
  return String(title ?? '').trim().slice(0, 200);
}
