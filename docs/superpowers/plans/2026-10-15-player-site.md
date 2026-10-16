# Player Site and "Copy player link" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A public player page on GitHub Pages that plays a pasted YouTube video full-window, remembers the exact second, and switches seamlessly to a re-upload if the video disappears; plus a "Copy player link" action in every app.

**Architecture:** Static site (`site/`, plain ES modules, YouTube IFrame Player API on youtube-nocookie) published by a Pages workflow; a Cloudflare Worker (`relay/`) that runs YouTube's `youtubei/v1/search` server-side for the site; a `PlayerLink` helper in Core used by the desktop GUI, the CLI and both phone apps.

**Tech Stack:** C# / .NET 10 / Avalonia 12 (apps), xUnit 2.9.3 (Core tests), vanilla JS + `node --test` (site and relay tests), Cloudflare Workers + Wrangler 4, GitHub Actions Pages.

**Spec:** `docs/superpowers/specs/2026-10-15-player-site-design.md`

## Global Constraints

- Player link format: `https://nitro70.github.io/YoutubeDownloader/?v=VIDEO_ID`, plus `&t=SECONDS` when the source link had `t` or `start` (forms `90`, `90s`, `1m30s`, `1h2m3s`).
- Embedded player host: `https://www.youtube-nocookie.com`; the page sends `Referer` (`<meta name="referrer" content="strict-origin-when-cross-origin">`).
- Saved time: `localStorage` key `ytp:v:ID`, value `{ id, title, channel, channelUrl, duration, time, updated }`; written every 2 s while playing and on `pagehide`/`visibilitychange`; explicit `t` wins; saved time ignored within 5 s of the end.
- Removal check: oEmbed every 60 s while visible and at once on a player error; "gone" = status 400/401/403/404 for a video oEmbed knew before. Player errors 101/150 = embedding disabled, not gone.
- Re-upload match: different id; same channel (handle or normalized name); title similarity (word-set Dice) >= 0.85; duration within 3 s when both known, else similarity >= 0.95.
- Switch: second muted player at the old time, re-seek on PLAYING, 1 s crossfade of opacity and volume, then destroy old player and delete its entry.
- Relay: `GET /search?q=` (1 to 200 chars), up to 10 results `{ id, title, channel, channelId, channelUrl, lengthSeconds }`; CORS only for `https://nitro70.github.io` and `http://localhost` / `http://127.0.0.1` any port; 10 minute cache; 502 on YouTube failure, 400 on bad q, 403 on other origins.
- Test hooks only on `localhost` / `127.0.0.1`.
- Writing rules (Andrew's): no em or en dashes anywhere (code, comments, docs, commits, UI text); no AI/Claude attribution in commits or release notes; commits use the configured git identity.

## Review Focus

- A shared `?v=` link opened fresh (no click yet): browsers block autoplay with sound, so the page must cue the video at the right second and wait for a tap, not show an error. Test in Task 7.
- A transient oEmbed failure (offline, 5xx, timeout) must not be treated as "gone". `isGoneStatus` unit test in Task 5.
- The relay unreachable or failing: the page must say it couldn't search and keep the paste box, not hang or throw. Browser test with a bad relay URL in Task 7.
- A watch link that also has `&list=...` must play the video, not be refused. Unit tests in Tasks 1 and 5.
- A very long title must not make the relay call fail (q over 200 characters). `searchQuery` unit test in Task 5.

---

## File Structure

| File | Responsibility |
|---|---|
| `YouTubeDownloader.Core/PlayerLink.cs` | Build the player link from any YouTube video link |
| `tests/YouTubeDownloader.Core.Tests/*` | xUnit tests for `PlayerLink` |
| `YouTubeDownloader/CliRunner.cs` | `-p/--player-link` option and help text |
| `YouTubeDownloader/MainWindow.axaml(.cs)` | "Copy player link" button in the video card |
| `YouTubeDownloader.iOS/MainView.axaml(.cs)`, `YouTubeDownloader.Android/MainView.axaml(.cs)` | "Copy player link" button in the video card |
| `site/logic.js` | Pure page logic: link parsing, times, matching (no DOM) |
| `site/logic.test.mjs`, `site/test-fixtures/relay-me-at-the-zoo.json` | Unit tests and relay sample |
| `site/index.html`, `site/style.css`, `site/app.js`, `site/config.js`, `site/package.json` | The page |
| `relay/src/parse.js`, `relay/src/index.js`, `relay/wrangler.toml`, `relay/package.json` | The Worker |
| `relay/test/parse.test.mjs`, `relay/test/fixtures/innertube-search-me-at-the-zoo.json` | Relay parser tests and trimmed real YouTube answer |
| `.github/workflows/pages.yml` | Test and publish `site/` |
| `.github/workflows/release.yml` | Run Core tests in the linux job |

---

### Task 1: PlayerLink in Core, with tests

**Files:**
- Create: `YouTubeDownloader.Core/PlayerLink.cs`
- Create: `tests/YouTubeDownloader.Core.Tests/YouTubeDownloader.Core.Tests.csproj`
- Create: `tests/YouTubeDownloader.Core.Tests/PlayerLinkTests.cs`
- Modify: `.github/workflows/release.yml` (linux job: run the tests)

**Interfaces:**
- Produces: `public static class PlayerLink { public const string PageUrl; public static bool TryCreate(string? text, out string link); }`

- [ ] **Step 1: Test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.11.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\YouTubeDownloader.Core\YouTubeDownloader.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Failing tests** (`PlayerLinkTests.cs`)

```csharp
using YouTubeDownloader.Core;

namespace YouTubeDownloader.Core.Tests;

public class PlayerLinkTests
{
    private const string Page = "https://nitro70.github.io/YoutubeDownloader/";

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://youtu.be/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("youtu.be/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://m.youtube.com/watch?v=jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/shorts/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/embed/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/live/jNQXAC9IVRw", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&list=PL1234567890", Page + "?v=jNQXAC9IVRw")]
    [InlineData("https://youtu.be/jNQXAC9IVRw?t=42", Page + "?v=jNQXAC9IVRw&t=42")]
    [InlineData("https://youtu.be/jNQXAC9IVRw?t=90s", Page + "?v=jNQXAC9IVRw&t=90")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&t=1m30s", Page + "?v=jNQXAC9IVRw&t=90")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&t=1h2m3s", Page + "?v=jNQXAC9IVRw&t=3723")]
    [InlineData("https://www.youtube.com/embed/jNQXAC9IVRw?start=15", Page + "?v=jNQXAC9IVRw&t=15")]
    [InlineData("https://youtu.be/jNQXAC9IVRw?t=0", Page + "?v=jNQXAC9IVRw")]
    public void Builds_the_player_link(string input, string expected)
    {
        Assert.True(PlayerLink.TryCreate(input, out string link));
        Assert.Equal(expected, link);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("me at the zoo")]
    [InlineData("https://www.youtube.com/playlist?list=PL1234567890")]
    [InlineData("https://www.youtube.com/@jawed")]
    [InlineData("https://vimeo.com/123456")]
    public void Refuses_anything_but_a_video_link(string? input)
    {
        Assert.False(PlayerLink.TryCreate(input, out string link));
        Assert.Equal(string.Empty, link);
    }
}
```

- [ ] **Step 3: Run, expect a compile failure** (`PlayerLink` doesn't exist)

Run: `dotnet test tests/YouTubeDownloader.Core.Tests -c Release`
Expected: build error CS0103 `PlayerLink`.

- [ ] **Step 4: Implement** (`YouTubeDownloader.Core/PlayerLink.cs`)

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using YoutubeExplode.Videos;

namespace YouTubeDownloader.Core;

/// <summary>
/// Links to the project's player page on GitHub Pages, which plays a video full-window through
/// YouTube's embedded player. A plain embed link no longer plays when typed into a browser
/// (YouTube answers "Error 153" without a Referer), so this is the link to hand out instead.
/// </summary>
public static class PlayerLink
{
    public const string PageUrl = "https://nitro70.github.io/YoutubeDownloader/";

    private static readonly Regex LivePath = new(@"youtube\.com/live/([A-Za-z0-9_-]{11})", RegexOptions.IgnoreCase);
    private static readonly Regex Duration = new(@"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$");

    /// <summary>
    /// The player page link for a YouTube video link, keeping its start time (t or start).
    /// False for search words or any link that isn't to a single video.
    /// </summary>
    public static bool TryCreate(string? text, out string link)
    {
        link = string.Empty;
        if (!YouTubeInput.TryGetUrl(text, out string url)) return false;

        string? id = VideoId.TryParse(url)?.Value;
        id ??= LivePath.Match(url) is { Success: true } m ? m.Groups[1].Value : null;
        if (id is null) return false;

        link = PageUrl + "?v=" + id;
        if (StartSeconds(url) is int start && start > 0) link += "&t=" + start.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static int? StartSeconds(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string key = part[..eq];
            if (key is "t" or "start") return ParseTime(Uri.UnescapeDataString(part[(eq + 1)..]));
        }
        return null;
    }

    // 90, 90s, 1m30s, 1h2m3s
    private static int? ParseTime(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)) return seconds;
        var m = Duration.Match(value);
        if (value.Length == 0 || !m.Success) return null;
        int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        return Part(1) * 3600 + Part(2) * 60 + Part(3);
    }
}
```

- [ ] **Step 5: Run tests, expect all to pass**

Run: `dotnet test tests/YouTubeDownloader.Core.Tests -c Release`
Expected: `Passed!  - Failed: 0, Passed: 20`.

- [ ] **Step 6: Run the tests in CI** (`.github/workflows/release.yml`, linux job, after the NuGet cache step)

```yaml
      - name: Core tests
        run: dotnet test tests/YouTubeDownloader.Core.Tests -c Release
```

- [ ] **Step 7: Commit**

```bash
git add YouTubeDownloader.Core/PlayerLink.cs tests/YouTubeDownloader.Core.Tests .github/workflows/release.yml
git commit -m "Core: PlayerLink builds the player page link for a video link"
```

---

### Task 2: CLI `-p, --player-link`

**Files:**
- Modify: `YouTubeDownloader/CliRunner.cs` (Run, a new PrintPlayerLink, CliOptions, PrintHelp)

**Interfaces:**
- Consumes: `PlayerLink.TryCreate(string?, out string)` (Task 1), `YouTubeService.SearchAsync(string, int, CancellationToken)`, existing `TryCopyToClipboard(string)`.

- [ ] **Step 1: Option** in `CliOptions`: field `public bool PlayerLink;` and in the switch:

```csharp
                    case "-p": case "--player-link":
                        o.PlayerLink = true; break;
```

- [ ] **Step 2: Route it** in `Run`, right after the `parseError` check and before `if (o.Search)`:

```csharp
        if (o.PlayerLink)
            return PrintPlayerLink(o);
```

- [ ] **Step 3: Implement** (next to `Search`):

```csharp
    // Prints the player page link for the link given, or for the first search result.
    private static int PrintPlayerLink(CliOptions o)
    {
        string? source = o.Url;
        if (source == null)
        {
            if (o.Query == null)
            {
                Console.Error.WriteLine("Error: --player-link needs a link or search words.");
                return 2;
            }
            try
            {
                var results = new YouTubeService().SearchAsync(o.Query, 1).GetAwaiter().GetResult();
                if (results.Count == 0)
                {
                    Console.Error.WriteLine($"No video found for \"{o.Query}\".");
                    return 1;
                }
                Console.Error.WriteLine($"Top result: {results[0].Title} ({results[0].Author})");
                source = results[0].Url;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Search failed: {ex.Message}");
                return 1;
            }
        }

        if (!PlayerLink.TryCreate(source, out string link))
        {
            Console.Error.WriteLine($"Error: not a link to a single YouTube video: {source}");
            return 2;
        }
        Console.WriteLine(link);
        if (TryCopyToClipboard(link))
            Console.Error.WriteLine("(copied to the clipboard)");
        return 0;
    }
```

- [ ] **Step 4: Help text**: under ACTIONS after `-r, --results`:

```
              -p, --player-link      Print a link that plays just the video in any
                                     browser (Windows: also copied to the clipboard)
```

and under EXAMPLES: `{exe} -p https://youtu.be/VIDEO`.

- [ ] **Step 5: Verify** (back up and restore the clipboard around these runs):

Run: `dotnet run --project YouTubeDownloader -c Release -- -p "https://youtu.be/jNQXAC9IVRw?t=5"`
Expected stdout: `https://nitro70.github.io/YoutubeDownloader/?v=jNQXAC9IVRw&t=5`, exit 0.
Run: `... -- -p me at the zoo` : stdout a player link, stderr `Top result: Me at the zoo (jawed)`.
Run: `... -- -p https://www.youtube.com/@jawed` : exit 2 with the error line.
Run: `... -- --help` : shows the new line.

- [ ] **Step 6: Commit**: `CLI: -p/--player-link prints the player page link`

---

### Task 3: "Copy player link" in the desktop app

**Files:**
- Modify: `YouTubeDownloader/MainWindow.axaml` (video card, column 2)
- Modify: `YouTubeDownloader/MainWindow.axaml.cs` (new click handler)

- [ ] **Step 1: XAML**: replace the single `CopyLinkButton` in `Grid.Column="2"` with:

```xml
                                <StackPanel Grid.Column="2" VerticalAlignment="Center"
                                            Spacing="6" Margin="10,0,0,0">
                                    <Button Name="CopyLinkButton"
                                            Content="🔗 Copy link" Classes="yt"
                                            HorizontalAlignment="Stretch"
                                            Padding="12,6" FontSize="12"
                                            Click="CopyLinkButton_Click"/>
                                    <Button Name="CopyPlayerLinkButton"
                                            Content="▶ Copy player link" Classes="yt"
                                            HorizontalAlignment="Stretch"
                                            Padding="12,6" FontSize="12"
                                            ToolTip.Tip="A link that plays just the video in any browser"
                                            Click="CopyPlayerLinkButton_Click"/>
                                </StackPanel>
```

- [ ] **Step 2: Handler** (after `CopyLinkButton_Click`):

```csharp
    private async void CopyPlayerLinkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!PlayerLink.TryCreate(UrlTextBox.Text, out string link) || Clipboard is null)
        {
            Log("Copy player link needs a link to a single YouTube video in the box.");
            return;
        }
        try
        {
            await Clipboard.SetTextAsync(link);
            Log($"Copied player link: {link}");
            CopyPlayerLinkButton.Content = "✓ Copied";
            await Task.Delay(1500);
            CopyPlayerLinkButton.Content = "▶ Copy player link";
        }
        catch (Exception ex)
        {
            Log($"Copy failed: {ex.Message}");
        }
    }
```

- [ ] **Step 3: Verify** with the headless GUI harness (Avalonia 12): search, pick the first result, click `CopyPlayerLinkButton`, read the headless clipboard with `TryGetTextAsync`; expect `https://nitro70.github.io/YoutubeDownloader/?v=<id>`; screenshot the card.

- [ ] **Step 4: Commit**: `Desktop: Copy player link button`

---

### Task 4: "Copy player link" on iPhone and Android

**Files:**
- Modify: `YouTubeDownloader.iOS/MainView.axaml`, `YouTubeDownloader.iOS/MainView.axaml.cs`
- Modify: `YouTubeDownloader.Android/MainView.axaml`, `YouTubeDownloader.Android/MainView.axaml.cs` (identical changes)

- [ ] **Step 1: XAML**: in `VideoInfoPanel`'s StackPanel, after `DurationText`:

```xml
                            <Button Name="CopyPlayerLinkButton"
                                    Content="▶  Copy player link" Classes="ghost"
                                    HorizontalAlignment="Stretch"
                                    Click="CopyPlayerLinkButton_Click"/>
```

- [ ] **Step 2: Handler** (after `PasteButton_Click`):

```csharp
    private async void CopyPlayerLinkButton_Click(object? sender, RoutedEventArgs e)
    {
        var clip = Top?.Clipboard;
        if (clip is null || !PlayerLink.TryCreate(UrlTextBox.Text, out string link)) return;
        try
        {
            await clip.SetTextAsync(link);
            CopyPlayerLinkButton.Content = "✓  Copied";
            await Task.Delay(1500);
            CopyPlayerLinkButton.Content = "▶  Copy player link";
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't copy: {ErrorText.Describe(ex)}");
        }
    }
```

- [ ] **Step 3: Verify**: iPhone-screen headless harness: Fetch Info for `https://youtu.be/jNQXAC9IVRw`, click the button, headless clipboard holds the player link; screenshot. Android: `dotnet build` of the Android project succeeds.

- [ ] **Step 4: Commit**: `Phones: Copy player link button`

---

### Task 5: Site logic and its tests

**Files:**
- Create: `site/package.json`, `site/logic.js`, `site/logic.test.mjs`, `site/test-fixtures/relay-me-at-the-zoo.json`

**Interfaces:**
- Produces (exports of `site/logic.js`): `parseVideoId(text) -> string|null`, `parseStart(text) -> number|null`, `parseTime(value) -> number|null`, `parseDuration(text) -> number|null`, `normalizeTitle(s) -> string`, `titleSimilarity(a, b) -> number`, `channelHandle(url) -> string|null`, `pickReplacement(gone, results) -> result|null`, `resumeTime(explicitT, saved) -> number`, `isGoneStatus(status) -> boolean`, `searchQuery(title) -> string`.

- [ ] **Step 1:** `site/package.json`: `{ "private": true, "type": "module" }`

- [ ] **Step 2: Fixture** `site/test-fixtures/relay-me-at-the-zoo.json`, relay output for "Me at the zoo jawed" (from a real search, 2026-10-15):

```json
[
  { "id": "jNQXAC9IVRw", "title": "Me at the zoo", "channel": "jawed", "channelId": "UC4QobU6STFB0P71PMvOGN5A", "channelUrl": "/@jawed", "lengthSeconds": 19 },
  { "id": "RD_Jwka9NaY", "title": "[ Me at the zoo ] where is Jawed Karim in 2026?", "channel": "Info-Beast", "channelId": "UCPs3YLdmB1yzT6_EwwFXe1g", "channelUrl": "/@InfoBeast-usa", "lengthSeconds": 213 },
  { "id": "ydEMNzksm2M", "title": "Me at the zoo - 20 Years Later", "channel": "Cody Hales", "channelId": "UC230eJhS0Oss2AiMVhlf9fQ", "channelUrl": "/@CodyHales", "lengthSeconds": 45 },
  { "id": "sQAqVABPQW8", "title": "Me at the zoo - 360° VR Video", "channel": "Multiverse 360VR", "channelId": "UCZ7JNc4Pqd6dZx87P9PDkEQ", "channelUrl": "/@Multiverse360vr", "lengthSeconds": 19 }
]
```

- [ ] **Step 3: Failing tests** (`site/logic.test.mjs`)

```js
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
```

- [ ] **Step 4: Run, expect failure**: `node --test site/` : `Cannot find module './logic.js'`.

- [ ] **Step 5: Implement** (`site/logic.js`)

```js
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
  return String(s ?? '').toLowerCase().normalize('NFKD').replace(/[̀-ͯ]/g, '')
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
```

- [ ] **Step 6: Run tests, expect all to pass**: `node --test site/` : `# pass 12`, `# fail 0`.

- [ ] **Step 7: Commit**: `Player site: link parsing, timing and re-upload matching (tested)`

---

### Task 6: The search relay (Cloudflare Worker)

**Files:**
- Create: `relay/package.json`, `relay/wrangler.toml`, `relay/src/parse.js`, `relay/src/index.js`
- Create: `relay/test/parse.test.mjs`, `relay/test/fixtures/innertube-search-me-at-the-zoo.json`

**Interfaces:**
- Produces: `GET <relay>/search?q=...` -> JSON array of `{ id, title, channel, channelId, channelUrl, lengthSeconds }`; the deployed origin URL (for `site/config.js`).

- [ ] **Step 1:** `relay/package.json`: `{ "private": true, "type": "module" }`; `relay/wrangler.toml`:

```toml
name = "ytd-relay"
main = "src/index.js"
compatibility_date = "2026-10-01"
workers_dev = true
```

- [ ] **Step 2: Fixture**: trim the real answer saved at `scratch-search/innertube-search.json` (2026-10-15, query "Me at the zoo jawed") to `contents.twoColumnSearchResultsRenderer.primaryContents.sectionListRenderer.contents[].itemSectionRenderer.contents[]` keeping the first 4 `videoRenderer` items (only `videoId`, `title`, `ownerText`, `lengthText`), the `channelRenderer` item reduced to `{ "channelRenderer": { "channelId": "x" } }`, and one `lockupViewModel` reduced to `{ "lockupViewModel": {} }`. Save as `relay/test/fixtures/innertube-search-me-at-the-zoo.json`.

- [ ] **Step 3: Failing test** (`relay/test/parse.test.mjs`)

```js
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
```

- [ ] **Step 4: Run, expect failure**: `node --test relay/` : module not found.

- [ ] **Step 5: Implement** `relay/src/parse.js`:

```js
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
```

- [ ] **Step 6: Run tests, expect pass**: `node --test relay/`.

- [ ] **Step 7: Worker** `relay/src/index.js`:

```js
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
```

- [ ] **Step 8: Local check**: `npx wrangler dev` in `relay/` (background), then:
  - `curl -s -H "Origin: http://localhost:8765" "http://127.0.0.1:8787/search?q=me%20at%20the%20zoo%20jawed"` : JSON list whose first item is `jNQXAC9IVRw` with `lengthSeconds` 19.
  - same with `-H "Origin: https://evil.example"` : 403.
  - `q=` empty and a 201-character `q` : 400.

- [ ] **Step 9: Deploy**: `npx wrangler deploy` in `relay/` (if it asks to register a workers.dev subdomain, run it in a terminal tab so Andrew can pick a name). Note the printed `https://ytd-relay.<subdomain>.workers.dev`. Repeat the three curl checks against it.

- [ ] **Step 10: Commit**: `Relay: Cloudflare Worker that searches YouTube for the player page`

---

### Task 7: The player page

**Files:**
- Create: `site/index.html`, `site/style.css`, `site/config.js`, `site/app.js`

**Interfaces:**
- Consumes: everything exported by `site/logic.js` (Task 5); the relay URL (Task 6).
- Produces: on localhost only, `window.ytpDebug = { state() }` returning `{ id, time, playerState, players }` for browser tests.

- [ ] **Step 1:** `site/config.js`: `export const RELAY_URL = 'https://ytd-relay.<subdomain>.workers.dev';` (the deployed address from Task 6).

- [ ] **Step 2:** `site/index.html`

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <!-- YouTube refuses embeds that arrive without a Referer (Error 153). -->
  <meta name="referrer" content="strict-origin-when-cross-origin">
  <title>YT Player</title>
  <link rel="stylesheet" href="style.css">
</head>
<body>
  <form id="bar" autocomplete="off">
    <input id="link" type="text" inputmode="url" placeholder="Paste a YouTube link" aria-label="YouTube link">
    <button type="submit">Play</button>
  </form>
  <p id="status" role="status"></p>
  <main id="stage">
    <p id="intro">Paste a YouTube link above to watch just the video.<br>
      Your place is remembered, and if the video is re-uploaded the page follows it.</p>
  </main>
  <script type="module" src="app.js"></script>
</body>
</html>
```

- [ ] **Step 3:** `site/style.css`

```css
:root { color-scheme: dark; --bg: #0f0f0f; --panel: #212121; --text: #f1f1f1; --muted: #aaa; --red: #ff0033; }
* { box-sizing: border-box; }
html, body { height: 100%; margin: 0; }
body { display: flex; flex-direction: column; background: var(--bg); color: var(--text);
  font: 15px/1.4 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
#bar { display: flex; gap: 8px; padding: 10px 12px; background: var(--panel); }
#link { flex: 1; min-width: 0; padding: 9px 12px; border: 1px solid #333; border-radius: 8px;
  background: var(--bg); color: var(--text); font: inherit; }
#link:focus { outline: 2px solid var(--red); outline-offset: -1px; }
#bar button { padding: 9px 18px; border: 0; border-radius: 8px; background: var(--red); color: #fff;
  font: inherit; font-weight: 600; cursor: pointer; }
#status { margin: 0; padding: 6px 12px; min-height: 1.4em; color: var(--muted); font-size: 13px; }
#status a { color: var(--text); }
#stage { position: relative; flex: 1; min-height: 0; }
#intro { margin: 15vh auto 0; max-width: 32em; padding: 0 16px; text-align: center; color: var(--muted); }
.player { position: absolute; inset: 0; transition: opacity 1s linear; }
.player iframe { width: 100%; height: 100%; border: 0; display: block; }
.player.entering { opacity: 0; }
```

- [ ] **Step 4:** `site/app.js`

```js
import {
  parseVideoId, parseStart, pickReplacement, resumeTime, isGoneStatus, searchQuery,
} from './logic.js';
import { RELAY_URL } from './config.js';

const SAVE_EVERY_MS = 2000;
const CHECK_EVERY_MS = 60000;
const FADE_MS = 1000;
const local = ['localhost', '127.0.0.1'].includes(location.hostname);
const params = new URLSearchParams(location.search);
const relayUrl = (local && params.get('debugRelay')) || RELAY_URL;

const $ = id => document.getElementById(id);
const stage = $('stage');
const status = text => { $('status').textContent = text; };

let current = null; // { id, player, el, info, known }

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
  if (!current?.player?.getCurrentTime) return;
  const time = current.player.getCurrentTime();
  if (!(time >= 0)) return;
  save({ ...current.info, id: current.id, time, duration: current.player.getDuration?.() || current.info.duration || null });
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
  for (const el of stage.querySelectorAll('.player')) el.remove();
  $('intro')?.remove();
  const saved = loadSaved(id);
  const at = resumeTime(start, saved);
  const info = { title: saved?.title ?? null, channel: saved?.channel ?? null,
    channelUrl: saved?.channelUrl ?? null, duration: saved?.duration ?? null };
  current = { id, player: null, el: null, info, known: !!saved?.title };
  history.replaceState(null, '', '?v=' + id);
  status(at > 0 ? `Resuming at ${formatTime(at)}.` : '');

  const { player, el } = await createPlayer(id, at, { autoplay, muted: false, entering: false });
  if (current?.id !== id) { player.destroy(); el.remove(); return; }
  current.player = player;
  current.el = el;

  const meta = await oembed(id);
  if (meta.status === 200 && current?.id === id) {
    Object.assign(current.info, { title: meta.title, channel: meta.channel, channelUrl: meta.channelUrl });
    current.known = true;
    document.title = meta.title + ' | YT Player';
    saveNow();
  } else if (isGoneStatus(meta.status) && current?.known) {
    handleGone();
  }
}

function onStateChange(player, state) {
  if (player !== current?.player) return;
  if (state === 1 /* playing */ || state === 2 /* paused */) saveNow();
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
  if (!current?.known) return;
  const meta = await oembed(current.id);
  if (isGoneStatus(meta.status)) await handleGone();
}

// ---------- re-uploads ----------
let searching = false;
async function handleGone({ debugSameVideo = false } = {}) {
  if (!current || searching) return;
  searching = true;
  try {
    saveNow();
    const gone = { ...(loadSaved(current.id) ?? {}), ...current.info, id: debugSameVideo ? 'debug-gone' : current.id };
    status('This video was removed. Looking for a re-upload...');
    let next = local ? params.get('debugReplaceWith') : null;
    if (!next) {
      let results;
      try {
        const r = await fetch(relayUrl + '/search?q=' + encodeURIComponent(searchQuery(gone.title)));
        if (!r.ok) throw new Error(String(r.status));
        results = await r.json();
      } catch {
        status('This video was removed, and searching for a re-upload failed. Paste a new link above.');
        return;
      }
      next = pickReplacement(gone, results)?.id ?? null;
    }
    if (!next) {
      status('This video was removed and no re-upload was found. Paste a new link above.');
      return;
    }
    await switchTo(next);
    status('The video was replaced; continuing with the new upload.');
  } finally {
    searching = false;
  }
}

/** Moves playback to `newId` at the same second, crossfading if the old player is still playing. */
async function switchTo(newId) {
  const old = current;
  const oldPlaying = old.player?.getPlayerState?.() === 1;
  const time = old.player?.getCurrentTime?.() ?? loadSaved(old.id)?.time ?? 0;
  const volume = old.player?.getVolume?.() ?? 100;
  const wasMuted = old.player?.isMuted?.() ?? false;

  const { player, el } = await createPlayer(newId, time, { autoplay: oldPlaying, muted: true, entering: oldPlaying });
  const next = { id: newId, player, el, info: { ...old.info, duration: null }, known: false };

  if (oldPlaying) {
    await waitForState(player, 1, 15000);
    player.seekTo(old.player.getCurrentTime(), true); // absorb the time the new one took to start
    await waitForState(player, 1, 5000);
    if (!wasMuted) player.unMute();
    player.setVolume(0);
    el.classList.remove('entering');
    await fade(FADE_MS, k => {
      player.setVolume(Math.round(volume * k));
      old.player.setVolume(Math.round(volume * (1 - k)));
    });
  } else if (!wasMuted) {
    player.unMute();
  }

  old.player?.destroy();
  old.el?.remove();
  removeSaved(old.id);
  current = next;
  history.replaceState(null, '', '?v=' + newId);
  save({ ...next.info, id: newId, time });
  const meta = await oembed(newId);
  if (meta.status === 200 && current?.id === newId) {
    Object.assign(current.info, { title: meta.title, channel: meta.channel, channelUrl: meta.channelUrl });
    current.known = true;
    saveNow();
  }
}

function waitForState(player, state, timeoutMs) {
  return new Promise(resolve => {
    const started = Date.now();
    const tick = () => {
      if (player.getPlayerState?.() === state || Date.now() - started > timeoutMs) return resolve();
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
```

- [ ] **Step 5: Browser checks** (local server: `python -m http.server 8765 --directory site`, in the background; the built-in browser pane):
  1. `http://localhost:8765/` : intro text; paste `https://youtu.be/jNQXAC9IVRw` and Play : one `.player`, `ytpDebug.state().playerState` becomes 1; screenshot.
  2. Play to about 8 s, reload `?v=jNQXAC9IVRw` : status "Resuming at 0:0x", player cued (state 5 or 2), `getCurrentTime()` within 1 s of the saved time once played.
  3. `?v=jNQXAC9IVRw&t=3` : starts at 3 s even though a later time is saved.
  4. Play `dQw4w9WgXcQ` with `&debugGoneAfter=6&debugReplaceWith=jNQXAC9IVRw` after pressing Play: after the switch `state().id === 'jNQXAC9IVRw'`, one `.player` left, `localStorage['ytp:v:dQw4w9WgXcQ']` gone, time within 1.5 s of the time at the switch.
  5. `?v=jNQXAC9IVRw&debugGoneAfter=6` (real relay, same video found as its own re-upload): status "replaced", playback continues near the same second.
  6. `&debugRelay=http://127.0.0.1:1&debugGoneAfter=4` : status "searching for a re-upload failed", paste box still works.
  7. Paste `me at the zoo` : "That isn't a link to a YouTube video."
  8. Phone width (375x812): the bar fits, the player fills the rest; screenshot.

- [ ] **Step 6: Commit**: `Player site: the page (paste, remember the second, follow re-uploads)`

---

### Task 8: Publish on GitHub Pages

**Files:**
- Create: `.github/workflows/pages.yml`

- [ ] **Step 1: Workflow**

```yaml
name: Player site

on:
  push:
    branches: [main]
    paths: ['site/**', '.github/workflows/pages.yml']
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: true

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '22'
      - name: Unit tests
        run: node --test site/
      - name: Collect the site (without tests)
        run: |
          mkdir _site
          rsync -a --exclude '*.test.mjs' --exclude 'test-fixtures' --exclude 'package.json' site/ _site/
      - uses: actions/configure-pages@v5
      - uses: actions/upload-pages-artifact@v3
        with:
          path: _site
      - id: deployment
        uses: actions/deploy-pages@v4
```

- [ ] **Step 2: Turn Pages on** (GitHub Actions source): `gh api -X POST repos/Nitro70/YoutubeDownloader/pages -f build_type=workflow` (if it already exists: `gh api -X PUT ... -f build_type=workflow`).

- [ ] **Step 3: After merging to main**, watch the "Player site" run; then repeat checks 1, 2 and 7 of Task 7 on `https://nitro70.github.io/YoutubeDownloader/`, and confirm a relay search from that origin works (check 5 without the debug hook: run `fetch(RELAY_URL + '/search?q=me at the zoo')` from the page's console).

- [ ] **Step 4: Commit** (before merging): `Pages workflow for the player site`

---

### Task 9: Version 4.1.0, README, release

**Files:**
- Modify: `YouTubeDownloader/YouTubeDownloader.csproj` (4.1.0), `YouTubeDownloader/MainWindow.axaml` (footer v4.1), `YouTubeDownloader.Android/YouTubeDownloader.Android.csproj` (ApplicationVersion 7, 4.1.0), `YouTubeDownloader.iOS/Info.plist` (4.1.0), `README.md`

- [ ] **Step 1: README**: a "Player page" section (what it does, the link format, remembering the second, following re-uploads, the limits), the `-p, --player-link` line in the CLI block and an example, and the button in the feature list.
- [ ] **Step 2: Versions** as listed.
- [ ] **Step 3: Merge** `player-site` into `main` (fast-forward), push; Pages deploys (Task 8 Step 3).
- [ ] **Step 4: Tag** `v4.1.0`, push; when the release run is green, set the title and notes, download the four files and check versions (APK versionName 4.1.0 code 7, IPA 4.1.0 with both frameworks, exe 4.1.0 with `-p` working).
- [ ] **Step 5: Vault**: session entry, project note, lesson (embeds need a Referer since 2025; oEmbed is CORS-friendly; InnerTube search needs a server-side relay).
