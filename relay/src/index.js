// Search relay for the player page. Browsers can't read YouTube's search answers from another
// site, so this makes the same youtubei/v1/search request as yt.bat, server-side, and returns
// plain videos. Only the player page (and localhost, for testing) may use it.
import { parseSearch } from './parse.js';

const ALLOWED = [/^https:\/\/nitro70\.github\.io$/, /^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/];
const CACHE_SECONDS = 600;

export default {
  async fetch(request, env, ctx) {
    const origin = request.headers.get('Origin') ?? '';
    const allowed = ALLOWED.some(r => r.test(origin));
    const cors = allowed ? { 'Access-Control-Allow-Origin': origin, Vary: 'Origin' } : {};

    if (request.method === 'OPTIONS') {
      return new Response(null, {
        status: allowed ? 204 : 403,
        headers: { ...cors, 'Access-Control-Allow-Methods': 'GET', 'Access-Control-Max-Age': '86400' },
      });
    }
    if (!allowed) return json({ error: 'origin not allowed' }, 403);

    const url = new URL(request.url);
    if (request.method !== 'GET' || url.pathname !== '/search') return json({ error: 'not found' }, 404, cors);
    const q = (url.searchParams.get('q') ?? '').trim();
    if (q.length < 1 || q.length > 200) return json({ error: 'q must be 1 to 200 characters' }, 400, cors);

    const cache = caches.default;
    const key = new Request('https://ytd-relay.cache/search?q=' + encodeURIComponent(q.toLowerCase()));
    const hit = await cache.match(key);
    if (hit) return withHeaders(hit, cors);

    let results;
    try {
      const yt = await fetch('https://www.youtube.com/youtubei/v1/search?prettyPrint=false', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          context: { client: { clientName: 'WEB', clientVersion: '2.20260101.00.00', hl: 'en', gl: 'US' } },
          query: q,
        }),
      });
      if (!yt.ok) return json({ error: `YouTube answered ${yt.status}` }, 502, cors);
      results = parseSearch(await yt.json()).slice(0, 10);
    } catch {
      return json({ error: 'YouTube could not be reached' }, 502, cors);
    }

    const res = json(results, 200, { 'Cache-Control': `public, max-age=${CACHE_SECONDS}` });
    ctx.waitUntil(cache.put(key, res.clone()));
    return withHeaders(res, cors);
  },
};

function json(body, status, headers = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json; charset=utf-8', ...headers },
  });
}

function withHeaders(res, headers) {
  const out = new Response(res.body, res);
  for (const [k, v] of Object.entries(headers)) out.headers.set(k, v);
  return out;
}
