import {
  parseVideoId, parseStart, pickReplacement, resumeTime, isGoneStatus, searchQuery,
  positionToKeep, switchMode, videoBox, aspectFrom,
} from './logic.js';
import { RELAY_URL } from './config.js';

const SAVE_EVERY_MS = 2000;
const CHECK_EVERY_MS = 60000;
const FADE_MS = 1000;
const OLD_PLAYER_LINGER_MS = 5000;
const PARK_AHEAD_S = 2;
const IDLE_MS = 2500;
const STATUS_MS = 5000;
const DOUBLE_CLICK_MS = 250;
const PLAY_ICON = '<svg viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>';
const REPLAY_ICON = '<svg viewBox="0 0 24 24"><path d="M12 5V2L7 6l5 4V7a5 5 0 1 1-5 5H5a7 7 0 1 0 7-7z"/></svg>';
const local = ['localhost', '127.0.0.1'].includes(location.hostname);
const params = new URLSearchParams(location.search);
const relayUrl = (local && params.get('debugRelay')) || RELAY_URL;

const $ = id => document.getElementById(id);
const stage = $('stage');
const shield = $('shield');
function status(text) {
  $('status').textContent = text;
  if (text) wake(STATUS_MS);
}

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

/**
 * A player for `id` in a new layer of the stage, sized to the picture and covered by the video's
 * own still until it plays (YouTube's waiting screen has its title and logo); resolves once ready.
 */
async function createPlayer(id, start, { autoplay, muted, entering, aspect }) {
  const YT = await api;
  const el = document.createElement('div');
  el.className = 'player' + (entering ? ' entering' : '');
  el.dataset.aspect = String(aspect);
  const holder = document.createElement('div');
  const cover = document.createElement('div');
  cover.className = 'cover';
  cover.innerHTML = '<span class="glyph">' + PLAY_ICON + '</span>';
  showStill(cover, id);
  el.append(holder, cover);
  stage.insertBefore(el, shield);
  layout(el);
  return new Promise(resolve => {
    const player = new YT.Player(holder, {
      host: 'https://www.youtube-nocookie.com',
      videoId: id,
      // No controls, no keyboard, no annotations, no other channels' videos at the end, no captions.
      playerVars: {
        start: Math.floor(start), autoplay: autoplay ? 1 : 0, mute: muted ? 1 : 0, playsinline: 1,
        controls: 0, disablekb: 1, fs: 0, iv_load_policy: 3, rel: 0, cc_load_policy: 0,
      },
      events: {
        onReady: () => resolve({ player, el }),
        onStateChange: e => onStateChange(player, el, e.data),
        onError: e => onPlayerError(player, e.data),
      },
    });
  });
}

function showStill(cover, id) {
  // `id` has passed parseVideoId, so it is 11 characters of [A-Za-z0-9_-].
  cover.style.backgroundImage = `url(https://i.ytimg.com/vi/${id}/hqdefault.jpg)`;
  const big = new Image();
  big.onload = () => { if (big.naturalWidth > 480) cover.style.backgroundImage = `url(${big.src})`; };
  big.src = `https://i.ytimg.com/vi/${id}/maxresdefault.jpg`;
}

function layout(el) {
  const box = videoBox(stage.clientWidth, stage.clientHeight, Number(el.dataset.aspect) || 16 / 9);
  Object.assign(el.style, { left: box.left + 'px', top: box.top + 'px', width: box.width + 'px', height: box.height + 'px' });
  el.style.setProperty('--crop', box.crop + 'px');
}

function setAspect(el, aspect) {
  if (!el) return;
  el.dataset.aspect = String(aspect);
  layout(el);
}

function captionsOff(player) {
  try {
    player.unloadModule?.('captions');
    player.setOption?.('captions', 'track', {});
  } catch { /* not loaded */ }
}

// ---------- oEmbed: title, channel, shape, and whether the video still exists ----------
async function oembed(id) {
  try {
    const r = await fetch('https://www.youtube.com/oembed?format=json&url=' +
      encodeURIComponent('https://www.youtube.com/watch?v=' + id));
    if (!r.ok) return { status: r.status };
    const j = await r.json();
    return { status: 200, title: j.title, channel: j.author_name, channelUrl: j.author_url,
      aspect: aspectFrom(j.width, j.height) };
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
    channelUrl: saved?.channelUrl ?? null, duration: saved?.duration ?? null, aspect: saved?.aspect ?? 16 / 9 };
  // Pressing Play counts as watching; a link opened cold waits for a tap.
  const mine = { id, player: null, el: null, info, known: !!saved?.title, watching: autoplay };
  current = mine;
  syncShield();
  history.replaceState(null, '', '?v=' + id);
  status(at > 0 ? `Resuming at ${formatTime(at)}.` : '');

  const { player, el } = await createPlayer(id, at, { autoplay, muted: false, entering: false, aspect: info.aspect });
  if (current !== mine) { destroyPlayer(player); el.remove(); return; }
  mine.player = player;
  mine.el = el;

  const meta = await oembed(id);
  if (current !== mine) return;
  if (meta.status === 200) {
    Object.assign(mine.info, { title: meta.title, channel: meta.channel, channelUrl: meta.channelUrl, aspect: meta.aspect });
    setAspect(el, meta.aspect);
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

function onStateChange(player, el, state) {
  const cover = el.querySelector('.cover');
  if (state === 1 /* playing */) {
    captionsOff(player);
    el.classList.add('started');
    cover.hidden = true;
  } else if (state === 0 /* ended */) {
    // Our own end card instead of YouTube's wall of other videos.
    cover.querySelector('.glyph').innerHTML = REPLAY_ICON;
    cover.hidden = false;
  }
  if (player !== current?.player) return;
  syncShield();
  if (state === 1) current.watching = true;
  else if (state === 2 /* paused */ || state === 0) { current.watching = false; wake(); }
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
    wake(STATUS_MS);
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
    if (next && parseVideoId(next) !== next) next = null; // only a plain video id goes into URLs
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
    { autoplay: mode !== 'cue', muted: fading || wasMuted, entering: fading, aspect: old.info.aspect });
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
    // A seek while playing lands late by however long it buffers. So park the new player, paused,
    // a little ahead of the old one, let it buffer there, and start it as the old one arrives.
    const meet = old.player.getCurrentTime() + PARK_AHEAD_S;
    player.pauseVideo();
    player.seekTo(meet, true);
    await waitFor(() => old.player.getCurrentTime() >= meet - 0.05, (PARK_AHEAD_S + 6) * 1000, movedOn);
    if (movedOn()) return abandon();
    player.playVideo();
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
  syncShield();
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
    Object.assign(next.info, { title: meta.title, channel: meta.channel, channelUrl: meta.channelUrl, aspect: meta.aspect });
    setAspect(el, meta.aspect);
    next.known = true;
    document.title = meta.title + ' | YT Player';
    saveNow();
  }
  return true;
}

/** Resolves once `test()` is true, after `timeoutMs`, or as soon as `cancelled()`. */
function waitFor(test, timeoutMs, cancelled = () => false) {
  return new Promise(resolve => {
    const started = Date.now();
    const tick = () => {
      let done = false;
      try { done = test(); } catch { /* a removed player */ }
      if (done || cancelled() || Date.now() - started > timeoutMs) return resolve();
      setTimeout(tick, 40);
    };
    tick();
  });
}

function waitForState(player, state, timeoutMs, cancelled) {
  return waitFor(() => player.getPlayerState?.() === state, timeoutMs, cancelled);
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

// ---------- our own controls: nothing of YouTube's is ever shown ----------
// The shield covers the video once it has played, so every click and tap comes here.
function syncShield() {
  shield.hidden = !current?.el?.classList.contains('started');
}

function togglePlay() {
  const p = current?.player;
  if (!p?.getPlayerState) return;
  const s = p.getPlayerState();
  if (s === 1 || s === 3) p.pauseVideo();
  else {
    if (s === 0) p.seekTo(0, true);
    p.playVideo();
  }
}

function seekBy(seconds) {
  const p = current?.player;
  if (!p?.getCurrentTime) return;
  p.seekTo(Math.max(0, p.getCurrentTime() + seconds), true);
}

function toggleFullscreen() {
  const done = document.fullscreenElement ? document.exitFullscreen?.() : document.documentElement.requestFullscreen?.();
  done?.catch?.(() => { /* not allowed here (iPhone Safari) */ });
}

// The bar fades out after a moment of no mouse or touch while the video plays.
let idleTimer = 0;
function wake(ms = IDLE_MS) {
  document.body.classList.remove('idle');
  clearTimeout(idleTimer);
  idleTimer = setTimeout(sleepIfPlaying, ms);
}
function sleepIfPlaying() {
  const playing = current?.player?.getPlayerState?.() === 1;
  if (playing && !$('top').contains(document.activeElement)) document.body.classList.add('idle');
  else idleTimer = setTimeout(sleepIfPlaying, IDLE_MS);
}

let lastPointer = 'mouse';
let clickTimer = 0;
shield.addEventListener('pointerdown', e => { lastPointer = e.pointerType; });
shield.addEventListener('click', () => {
  const wasIdle = document.body.classList.contains('idle');
  wake();
  if (clickTimer) { // second click: fullscreen, and the first click's pause is cancelled
    clearTimeout(clickTimer);
    clickTimer = 0;
    toggleFullscreen();
    return;
  }
  if (wasIdle && lastPointer === 'touch') return; // the first tap on a phone just brings the bar back
  clickTimer = setTimeout(() => { clickTimer = 0; togglePlay(); }, DOUBLE_CLICK_MS);
});
document.addEventListener('mousemove', () => wake());
document.addEventListener('keydown', e => {
  wake();
  if (e.target === $('link') || e.ctrlKey || e.metaKey || e.altKey) return;
  switch (e.key) {
    case ' ': case 'k': togglePlay(); break;
    case 'ArrowRight': seekBy(5); break;
    case 'ArrowLeft': seekBy(-5); break;
    case 'l': seekBy(10); break;
    case 'j': seekBy(-10); break;
    case 'f': toggleFullscreen(); break;
    case 'm': { const p = current?.player; if (p?.isMuted) p.isMuted() ? p.unMute() : p.mute(); break; }
    default: return;
  }
  e.preventDefault();
});
addEventListener('resize', () => { for (const el of stage.querySelectorAll('.player')) layout(el); });

// ---------- page wiring ----------
$('bar').addEventListener('submit', e => {
  e.preventDefault();
  const text = $('link').value;
  const id = parseVideoId(text);
  if (!id) { status('That isn\'t a link to a YouTube video.'); return; }
  $('link').blur();
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
