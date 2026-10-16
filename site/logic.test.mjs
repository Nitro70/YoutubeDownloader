import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  parseVideoId, parseStart, parseTime, parseDuration, titleSimilarity, channelHandle,
  pickReplacement, resumeTime, isGoneStatus, searchQuery,
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
