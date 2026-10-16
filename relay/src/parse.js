/** Plain videos from a youtubei/v1/search answer. Shelves, channels, ads and the like are skipped. */
export function parseSearch(data) {
  const sections = data?.contents?.twoColumnSearchResultsRenderer?.primaryContents
    ?.sectionListRenderer?.contents ?? [];
  const out = [];
  for (const section of sections) {
    for (const item of section?.itemSectionRenderer?.contents ?? []) {
      const v = item?.videoRenderer;
      if (!v?.videoId) continue;
      const owner = v.ownerText?.runs?.[0];
      const browse = owner?.navigationEndpoint?.browseEndpoint;
      out.push({
        id: v.videoId,
        title: (v.title?.runs ?? []).map(r => r.text).join('') || v.title?.simpleText || '',
        channel: owner?.text ?? '',
        channelId: browse?.browseId ?? null,
        channelUrl: browse?.canonicalBaseUrl ?? null,
        lengthSeconds: toSeconds(v.lengthText?.simpleText),
      });
    }
  }
  return out;
}

// "4:05" or "1:02:03"; null for live videos, which have no length.
function toSeconds(text) {
  if (typeof text !== 'string' || !/^\d+(:\d{1,2}){1,2}$/.test(text)) return null;
  return text.split(':').reduce((sum, part) => sum * 60 + Number(part), 0);
}
