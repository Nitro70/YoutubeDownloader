import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseSearch } from '../src/parse.js';

const answer = JSON.parse(readFileSync(new URL('./fixtures/innertube-search-me-at-the-zoo.json', import.meta.url)));

test('keeps plain videos only, with id, title, channel and length', () => {
  const r = parseSearch(answer);
  assert.equal(r.length, 4);
  assert.deepEqual(r[0], {
    id: 'jNQXAC9IVRw', title: 'Me at the zoo', channel: 'jawed',
    channelId: 'UC4QobU6STFB0P71PMvOGN5A', channelUrl: '/@jawed', lengthSeconds: 19,
  });
});

test('tolerates odd answers', () => {
  assert.deepEqual(parseSearch({}), []);
  assert.deepEqual(parseSearch(null), []);
});
