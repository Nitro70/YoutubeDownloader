import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  parseVideoId, parseStart, parseTime, parseDuration, titleSimilarity, channelHandle,
  pickReplacement, resumeTime, isGoneStatus, searchQuery, positionToKeep, switchMode,
  videoBox, aspectFrom, looksLikeAd,
} from './logic.js';

const relay = JSON.parse(readFileSync(new URL('./test-fixtures/relay-me-at-the-zoo.json', import.meta.url)));
const gone = { id: 'AAAAAAAAAAA', title: 'Me at the zoo', channel: 'jawed', channelUrl: 'https://www.youtube.com/@jawed', duration: 19 };

test('parseVideoId accepts every link form and a bare id', () => {
  for (const s of [
    'https://www.youtube.com/watch?v=jNQXAC9IVRw', 'youtube.com/watch?v=jNQXAC9IVRw',
    'https://youtu.be/jNQXAC9IVRw?t=4', 'https://m.youtube.com/watch?v=jNQXAC9IVRw',
    'https://music.youtube.com/watch?v=jNQXAC9IVRw', 'https://www.youtube.com/shorts/jNQXAC9IVRw',
    'https://www.youtube.com/live/jNQXAC9IVRw', 'https://www.youtube.com/embed/jNQXAC9IVRw',
    'https://www.youtube-nocookie.com/embed/jNQXAC9IVRw',
    'https://www.youtube.com/watch?v=jNQXAC9IVRw&list=PL1234567890', ' jNQXAC9IVRw ',
  ]) assert.equal(parseVideoId(s), 'jNQXAC9IVRw', s);
});

test('parseVideoId refuses non-video input', () => {
  for (const s of ['', 'me at the zoo', 'https://www.youtube.com/@jawed',
    'https://www.youtube.com/playlist?list=PL1234567890', 'https://vimeo.com/123456', 'jNQXAC9IVR'])
    assert.equal(parseVideoId(s), null, s);
});

test('start times', () => {
  assert.equal(parseStart('https://youtu.be/x?t=90'), 90);
  assert.equal(parseStart('https://youtu.be/x?t=90s'), 90);
  assert.equal(parseStart('https://www.youtube.com/watch?v=x&t=1m30s'), 90);
  assert.equal(parseStart('https://www.youtube.com/embed/x?start=42'), 42);
  assert.equal(parseStart('https://youtu.be/x'), null);
  assert.equal(parseTime('1h2m3s'), 3723);
  assert.equal(parseTime('abc'), null);
});

test('durations', () => {
  assert.equal(parseDuration('0:19'), 19);
  assert.equal(parseDuration('1:02:03'), 3723);
  assert.equal(parseDuration(undefined), null);
});

test('title similarity ignores case, punctuation and emoji', () => {
  assert.equal(titleSimilarity('Me at the zoo', 'ME AT THE ZOO!! 🦒'), 1);
  assert.ok(titleSimilarity('Me at the zoo', 'Never gonna give you up') < 0.2);
});

test('channel handles', () => {
  assert.equal(channelHandle('https://www.youtube.com/@jawed'), '@jawed');
  assert.equal(channelHandle('/@Jawed'), '@jawed');
  assert.equal(channelHandle(null), null);
});

test('pickReplacement finds the re-upload from the same channel', () => {
  assert.equal(pickReplacement(gone, relay)?.id, 'jNQXAC9IVRw');
});

test('pickReplacement rejects the same id, other channels, other lengths and titles', () => {
  assert.equal(pickReplacement({ ...gone, id: 'jNQXAC9IVRw' }, relay), null);
  assert.equal(pickReplacement({ ...gone, channel: 'Someone', channelUrl: 'https://www.youtube.com/@someone' }, relay), null);
  assert.equal(pickReplacement({ ...gone, duration: 60 }, relay), null);
  assert.equal(pickReplacement({ ...gone, title: 'My trip to the zoo in 2005' }, relay), null);
  assert.equal(pickReplacement(gone, []), null);
});

test('pickReplacement without a known duration needs a near-identical title', () => {
  assert.equal(pickReplacement({ ...gone, duration: null }, relay)?.id, 'jNQXAC9IVRw');
  assert.equal(pickReplacement({ ...gone, duration: null, title: 'Me at the zoo again' }, relay), null);
});

test('resumeTime: explicit t wins, near the end starts over', () => {
  assert.equal(resumeTime(30, { time: 100, duration: 300 }), 30);
  assert.equal(resumeTime(null, { time: 100, duration: 300 }), 100);
  assert.equal(resumeTime(null, { time: 297, duration: 300 }), 0);
  assert.equal(resumeTime(null, null), 0);
});

test('only definite answers count as gone', () => {
  for (const s of [400, 401, 403, 404]) assert.equal(isGoneStatus(s), true, String(s));
  for (const s of [0, 200, 429, 500, 503]) assert.equal(isGoneStatus(s), false, String(s));
});

test('searchQuery keeps the relay request under 200 characters', () => {
  assert.equal(searchQuery('x'.repeat(500)).length, 200);
  assert.equal(searchQuery('  Me at the zoo  '), 'Me at the zoo');
});

test('positionToKeep: a player that never loaded keeps the saved second', () => {
  assert.equal(positionToKeep(-1, 0, 300), 300);   // removed video: unstarted, reports 0
  assert.equal(positionToKeep(-1, 0, undefined), null);
  assert.equal(positionToKeep(1, 42.5, 300), 42.5); // playing
  assert.equal(positionToKeep(2, 7, null), 7);      // paused
  assert.equal(positionToKeep(3, 8, 1), 8);         // buffering
  assert.equal(positionToKeep(5, 10, 300), 10);     // cued at its start second
  assert.equal(positionToKeep(1, NaN, 5), 5);
});

test('switchMode: crossfade while playing, play on if the viewer was watching, else cue', () => {
  assert.equal(switchMode(1, true), 'crossfade');
  assert.equal(switchMode(3, true), 'crossfade');   // buffering counts as playing
  assert.equal(switchMode(-1, true), 'play');       // errored or never loaded while watching
  assert.equal(switchMode(0, true), 'play');
  assert.equal(switchMode(2, false), 'cue');        // paused by the viewer
  assert.equal(switchMode(5, false), 'cue');        // opened from a link, not started yet
});

test('videoBox fits the video and crops the bars off the YouTube frame', () => {
  assert.deepEqual(videoBox(960, 521, 16 / 9), { left: 17, top: 0, width: 926, height: 521, crop: 83 });
  assert.deepEqual(videoBox(375, 812, 16 / 9), { left: 0, top: 301, width: 375, height: 211, crop: 64 });
  assert.deepEqual(videoBox(1920, 1080, 4 / 3), { left: 240, top: 0, width: 1440, height: 1080, crop: 173 });
  assert.deepEqual(videoBox(390, 844, 9 / 16), { left: 0, top: 75, width: 390, height: 693, crop: 111 });
});

test('aspectFrom reads oEmbed sizes, 16:9 otherwise', () => {
  assert.equal(aspectFrom(200, 150), 4 / 3);
  assert.equal(aspectFrom(200, 113), 200 / 113);
  assert.equal(aspectFrom(undefined, 5), 16 / 9);
  assert.equal(aspectFrom(0, 0), 16 / 9);
});

test('looksLikeAd: another video, or running a while without our video moving', () => {
  const base = { state: 1, videoId: 'jNQXAC9IVRw', expectedId: 'jNQXAC9IVRw', stillMs: 0, watching: true };
  assert.equal(looksLikeAd(base), false);                                   // our video, moving
  assert.equal(looksLikeAd({ ...base, videoId: 'zzzzzzzzzzz' }), true);     // the player reports another video
  assert.equal(looksLikeAd({ ...base, stillMs: 2000 }), true);              // "playing" but our time stands still
  assert.equal(looksLikeAd({ ...base, state: 3, stillMs: 2000 }), true);    // buffering-like flapping
  assert.equal(looksLikeAd({ ...base, state: -1, stillMs: 2000 }), true);   // started by a tap, nothing of ours yet
  assert.equal(looksLikeAd({ ...base, stillMs: 1000 }), false);             // a short stall is not an ad
  assert.equal(looksLikeAd({ ...base, state: 2, stillMs: 9000 }), false);   // paused by the viewer
  assert.equal(looksLikeAd({ ...base, state: 5, stillMs: 9000 }), false);   // waiting for a first tap
  assert.equal(looksLikeAd({ ...base, state: 0, stillMs: 9000 }), false);   // ended
  assert.equal(looksLikeAd({ ...base, watching: false, stillMs: 9000 }), false); // nobody pressed play
});
