import {
  parseVideoId, parseStart, pickReplacement, resumeTime, isGoneStatus, searchQuery,
  positionToKeep, switchMode,
} from './logic.js';
import { RELAY_URL } from './config.js';

const SAVE_EVERY_MS = 2000;
const CHECK_EVERY_MS = 60000;
const FADE_MS = 1000;
const OLD_PLAYER_LINGER_MS = 5000;
const local = ['localhost', '127.0.0.1'].includes(location.hostname);
const params = new URLSearchParams(location.search);
const relayUrl = (local && params.get('debugRelay')) || RELAY_URL;

const $ = id => document.getElementById(id);
const stage = $('stage');
const status = text => { $('status').textContent = text; };

// { id, player, el, info, known, watching }. Replaced (never reused) for every video, so async
// work can tell whether the viewer moved on while it waited: `current !== mine`.
let current = null;

// ---------- saved positions ----------
const key = id => 'ytp:v:' + id;
function loadSaved(id) {
  try { return JSON.parse(localStorage.getItem(key(id))); } catch { return null; }
}
function save(entry) {
  try { localStorage.setItem(key(entry.id), JSON.stringify({ ...entry, updated: Date.now() })); } catch { /* private mode */ }
}
function removeSaved(id) {
  try { localStorage.removeItem(key(id)); } catch { /* ignore */ }
}
function saveNow() {
  const p = current?.player;
  if (!p?.getCurrentTime) return;
  const time = positionToKeep(p.getPlayerState?.(), p.getCurrentTime(), loadSaved(current.id)?.time);
  if (time === null) return;
  save({ ...current.info, id: current.id, time, duration: p.getDuration?.() || current.info.duration || null });
}

// ---------- YouTube IFrame API ----------
const api = new Promise(resolve => {
  window.onYouTubeIframeAPIReady = () => resolve(window.YT);
  const s = document.createElement('script');
  s.src = 'https://www.youtube.com/iframe_api';
  document.head.append(s);
});

/** A player for `id` in a new layer of the stage; resolves once it's ready. */
async function createPlayer(id, start, { autoplay, muted, entering }) {
  const YT = await api;
  const el = document.createElement('div');
  el.className = 'player' + (entering ? ' entering' : '');
  const holder = document.createElement('div');
  el.append(holder);
  stage.append(el);
  return new Promise(resolve => {
    const player = new YT.Player(holder, {
      host: 'https://www.youtube-nocookie.com',
      videoId: id,
      playerVars: { start: Math.floor(start), autoplay: autoplay ? 1 : 0, playsinline: 1, rel: 0, mute: muted ? 1 : 0 },
      events: {
        onReady: () => resolve({ player, el }),
        onStateChange: e => onStateChange(player, e.data),
        onError: e => onPlayerError(player, e.data),
      },
    });
  });
}

// ---------- oEmbed: title and channel, and whether the video still exists ----------
async function oembed(id) {
  try {
    const r = await fetch('https://www.youtube.com/oembed?format=json&url=' +
      encodeURIComponent('https://www.youtube.com/watch?v=' + id));
    if (!r.ok) return { status: r.status };
    const j = await r.json();
    return { status: 200, title: j.title, channel: j.author_name, channelUrl: j.author_url };
  } catch {
    return { status: 0 };
  }
}

// ---------- playing ----------
async function play(id, { start = null, autoplay = false } = {}) {
  destroyPlayer(current?.player);
  for (const el of stage.querySelectorAll('.player')) el.remove();
  $('intro')?.remove();
  const saved = loadSaved(id);
  const at = resumeTime(start, saved);
  const info = { title: saved?.title ?? null, channel: saved?.channel ?? null,
    channelUrl: saved?.channelUrl ?? null, duration: saved?.duration ?? null };
  // Pressing Play counts as watching; a link opened cold waits for a tap.
  const mine = { id, player: null, el: null, info, known: !!saved?.title, watching: autoplay };
  current = mine;
  history.replaceState(null, '', '?v=' + id);
  status(at > 0 ? `Resuming at ${formatTime(at)}.` : '');

  const { player, el } = await createPlayer(id, at, { autoplay, muted: false, entering: false });
  if (current !== mine) { destroyPlayer(player); el.remove(); return; }
  mine.player = player;
  mine.el = el;

  const meta = await oembed(id);
  if (current !== mine) return;
  if (meta.status === 200) {
    Object.assign(mine.info, { title: meta.title, channel: meta.channel, channelUrl: meta.channelUrl });
    mine.known = true;
    document.title = meta.title + ' | YT Player';
    saveNow();
  } else if (isGoneStatus(meta.status) && mine.known) {
    handleGone();
  }
}

function destroyPlayer(player) {
  try { player?.destroy(); } catch { /* its iframe is already gone */ }
}

function onStateChange(player, state) {
  if (player !== current?.player) return;
  if (state === 1 /* playing */) current.watching = true;
  else if (state === 2 /* paused */ || state === 0 /* ended */) current.watching = false;
  if (state === 1 || state === 2) saveNow();
}

async function onPlayerError(player, code) {
  if (player !== current?.player) return;
  if (code === 101 || code === 150) {
    $('status').innerHTML = '';
    $('status').append('The uploader doesn\'t allow this video to play on other sites. ',
      Object.assign(document.createElement('a'), {
        href: 'https://www.youtube.com/watch?v=' + current.id, textContent: 'Watch on YouTube', target: '_blank', rel: 'noopener',
      }));
    return;
  }
  await checkGone();
}

async function checkGone() {
  const mine = current;
  if (!mine?.known) return;
  const meta = await oembed(mine.id);
  if (current === mine && isGoneStatus(meta.status)) await handleGone();
}

// ---------- re-uploads ----------
let searching = false;
async function handleGone({ debugSameVideo = false } = {}) {
  const mine = current;
  if (!mine || searching) return;
  searching = true;
  try {
    saveNow();
    const gone = { ...(loadSaved(mine.id) ?? {}), ...mine.info, id: debugSameVideo ? 'debug-gone' : mine.id };
    status('This video was removed. Looking for a re-upload...');
    let next = local ? params.get('debugReplaceWith') : null;
    if (!next) {
      let results = null;
      try {
        const r = await fetch(relayUrl + '/search?q=' + encodeURIComponent(searchQuery(gone.title)));
        if (r.ok) results = await r.json();
      } catch { /* reported below */ }
      if (current !== mine) return; // the viewer pasted another link while we searched
      if (!results) {
        status('This video was removed, and searching for a re-upload failed. Paste a new link above.');
        return;
      }
      next = pickReplacement(gone, results)?.id ?? null;
    }
    if (!next) {
      status('This video was removed and no re-upload was found. Paste a new link above.');
      return;
    }
    if (await switchTo(next)) status('The video was replaced; continuing with the new upload.');
  } finally {
    searching = false;
  }
}

/**
 * Moves playback to `newId` at the same second: crossfading if the old player is playing, playing
 * on if the viewer was watching it, else cued. False if the viewer pasted another link meanwhile.
 */
async function switchTo(newId) {
  const old = current;
  const state = old.player?.getPlayerState?.() ?? -1;
  const mode = switchMode(state, old.watching);
  const fading = mode === 'crossfade';
  const time = positionToKeep(state, old.player?.getCurrentTime?.(), loadSaved(old.id)?.time) ?? 0;
  const volume = old.player?.getVolume?.() ?? 100;
  const wasMuted = old.player?.isMuted?.() ?? false;
  const movedOn = () => current !== old;

  const { player, el } = await createPlayer(newId, time,
    { autoplay: mode !== 'cue', muted: fading || wasMuted, entering: fading });
  const abandon = () => { destroyPlayer(player); el.remove(); return false; };
  if (movedOn()) return abandon();
  const next = { id: newId, player, el, info: { ...old.info, duration: null }, known: false, watching: mode !== 'cue' };

  // The pictures crossfade while the old sound fades out; the new player is unmuted only after the
  // old one has stopped, and the old one is destroyed a little later. Any other order (unmuting
  // the new player while the old one plays, or destroying the old one at once) makes the new
  // player stall for good in Chrome (seen 2026-10-15).
  if (fading) {
    await waitForState(player, 1, 15000, movedOn);
    if (movedOn()) return abandon();
    player.seekTo(old.player.getCurrentTime(), true); // absorb the time the new one took to start
    await waitForState(player, 1, 5000, movedOn);
    if (movedOn()) return abandon();
    el.classList.remove('entering');
    await fade(FADE_MS, k => { if (!movedOn()) old.player.setVolume(Math.round(volume * (1 - k))); });
    if (movedOn()) return abandon();
  }
  old.player?.stopVideo?.();
  if (old.el) old.el.style.visibility = 'hidden';
  setTimeout(() => { destroyPlayer(old.player); old.el?.remove(); }, OLD_PLAYER_LINGER_MS);

  removeSaved(old.id);
  current = next;
  history.replaceState(null, '', '?v=' + newId);
  save({ ...next.info, id: newId, time });
  if (fading && !wasMuted) {
    player.setVolume(0);
    player.unMute();
    await fade(FADE_MS / 2, k => { if (current === next) player.setVolume(Math.round(volume * k)); });
  } else {
    player.setVolume(volume);
  }
  const meta = await oembed(newId);
  if (current === next && meta.status === 200) {
    Object.assign(next.info, { title: meta.title, channel: meta.channel, channelUrl: meta.channelUrl });
    next.known = true;
    document.title = meta.title + ' | YT Player';
    saveNow();
  }
  return true;
}

/** Resolves when `player` reaches `state`, after `timeoutMs`, or as soon as `cancelled()`. */
function waitForState(player, state, timeoutMs, cancelled = () => false) {
  return new Promise(resolve => {
    const started = Date.now();
    const tick = () => {
      let now = null;
      try { now = player.getPlayerState?.(); } catch { /* removed */ }
      if (now === state || cancelled() || Date.now() - started > timeoutMs) return resolve();
      setTimeout(tick, 100);
    };
    tick();
  });
}

// Timers rather than animation frames: frames stop in a background tab, timers only slow down.
function fade(ms, step) {
  return new Promise(resolve => {
    const started = Date.now();
    const tick = () => {
      const k = Math.min(1, (Date.now() - started) / ms);
      step(k);
      if (k < 1) setTimeout(tick, 40); else resolve();
    };
    tick();
  });
}

function formatTime(s) {
  s = Math.floor(s);
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  return (h ? h + ':' + String(m).padStart(2, '0') : m) + ':' + String(sec).padStart(2, '0');
}

// ---------- page wiring ----------
$('bar').addEventListener('submit', e => {
  e.preventDefault();
  const text = $('link').value;
  const id = parseVideoId(text);
  if (!id) { status('That isn\'t a link to a YouTube video.'); return; }
  play(id, { start: parseStart(text), autoplay: true });
});

setInterval(() => { if (current?.player?.getPlayerState?.() === 1) saveNow(); }, SAVE_EVERY_MS);
setInterval(() => { if (document.visibilityState === 'visible') checkGone(); }, CHECK_EVERY_MS);
addEventListener('pagehide', saveNow);
document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') saveNow(); });

const startId = parseVideoId(params.get('v') ?? '');
if (startId) {
  const t = params.get('t');
  // Opened from a link: no click yet, so browsers would block sound. Cue it; a tap starts it.
  play(startId, { start: t == null ? null : Number(t) });
}

if (local) {
  const after = Number(params.get('debugGoneAfter'));
  if (after > 0) setTimeout(() => handleGone({ debugSameVideo: !params.get('debugReplaceWith') }), after * 1000);
  window.ytpDebug = {
    state: () => ({
      id: current?.id ?? null,
      time: current?.player?.getCurrentTime?.() ?? null,
      playerState: current?.player?.getPlayerState?.() ?? null,
      players: stage.querySelectorAll('.player').length,
    }),
  };
}
