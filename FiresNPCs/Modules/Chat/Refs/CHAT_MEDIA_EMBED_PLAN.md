# Chat Embedded Images / GIFs — Implementation Plan

## Overview

Detect image/GIF URLs in chat messages (from Discord relay or typed in-game), download the media, and display it inline in the chat window as a Unity UI `RawImage` element.

---

## Architecture

### Components

| File | Purpose |
|------|---------|
| `ChatMediaEmbed.cs` | MonoBehaviour attached to each embedded image in the chat. Handles download, GIF frame decoding, animation, and cleanup. |
| `ChatMediaDetector.cs` | Static utility — regex-detects image URLs in message text, returns URL + type (static image vs animated GIF). |
| `ChatMediaEmbedManager.cs` | Manages the pool of active embeds in the chat window. Handles creation, positioning below the message text, and cleanup when messages scroll out of the buffer. |
| `GifDecoder.cs` | Lightweight GIF89a frame decoder. Reads raw bytes, outputs `Texture2D[]` frames + `float[]` delays. No external dependencies. |

### Chat Patch Integration

In `ChatPatches.Chat_OnNewChatMessage_Postfix`:
1. Call `ChatMediaDetector.ExtractUrls(text)` on every incoming message
2. If URLs found, call `ChatMediaEmbedManager.QueueEmbed(url, type)` for each
3. The manager creates a `RawImage` GameObject below the last chat text entry

---

## Step-by-Step Implementation

### Step 1: URL Detection (`ChatMediaDetector.cs`)

```
Static class with:
- ExtractImageUrls(string text) ? List<(string url, MediaType type)>
- Regex matches: https?://....(png|jpg|jpeg|gif|webp)
- Also matches known providers:
    - tenor.com/view/... ? treat as GIF (resolve via Tenor oEmbed)
    - giphy.com/... ? treat as GIF
    - media.discordapp.net/... ? direct image
    - cdn.discordapp.com/... ? direct image
- Discord messages also include direct media URLs in the JSON
  (msg["attachments"] array) — extract those in DiscordBotListener
  before relaying
```

### Step 2: GIF Decoder (`GifDecoder.cs`)

```
Pure C# GIF89a parser:
- Input: byte[]
- Output: Texture2D[] frames, float[] frameDelays
- Handles: LZW decompression, frame disposal, local/global color tables
- Max frames cap (e.g. 100) to prevent memory abuse
- Max file size cap (e.g. 5MB)
- Runs synchronously on the raw bytes after download completes

Note: Unity's ImageConversion.LoadImage() can load a GIF but only
returns the first frame. For animation we need the full decoder.
```

### Step 3: Media Embed MonoBehaviour (`ChatMediaEmbed.cs`)

```
Lifecycle:
1. Created by ChatMediaEmbedManager as a child of the chat content area
2. Starts a coroutine: UnityWebRequestTexture.GetTexture(url)
3. On download complete:
   - If static image (png/jpg): apply texture to RawImage, done
   - If GIF: pass bytes to GifDecoder, get frames + delays
   - Start animation coroutine cycling through frames
4. Sizing: fit to max width (chat window width - padding),
   maintain aspect ratio, cap max height (e.g. 150px)
5. Cleanup: when the embed scrolls out of the chat buffer
   or the chat is cleared, destroy textures and GameObject

Fields:
- RawImage targetImage
- Texture2D[] gifFrames
- float[] frameDelays
- int currentFrame
- float frameTimer
- bool isAnimating
```

### Step 4: Embed Manager (`ChatMediaEmbedManager.cs`)

```
Responsibilities:
- Track active embeds (linked to chat buffer index)
- Create embed GameObjects in the correct position:
    - The chat uses a single Text component (m_output) for all messages
    - We CANNOT inject images inline in a Text component
    - Instead: create a panel BELOW the chat output text, overlaying
      the chat window, positioned relative to the last message line
    - OR: use a VerticalLayoutGroup wrapper around the chat content
      that interleaves Text entries and Image entries

Approach — Overlay Panel:
    - Create a RectTransform child of Chat_box/root
    - Position it at the bottom of the visible chat area
    - When new messages push old ones up, reposition or destroy
    - Simpler but images don't scroll with text

Approach — Content Replacement (recommended):
    - Replace m_output (single Text) with a ScrollRect + 
      VerticalLayoutGroup content area
    - Each chat message becomes its own Text element
    - Images are RawImage elements inserted between Text elements
    - This gives proper scrolling and interleaving
    - More invasive but correct behavior

Recommended: Overlay approach first (simpler), refactor to
content replacement later if needed.

Overlay approach detail:
    - Maintain a List<ChatMediaEmbed> of active embeds
    - Each embed is anchored to bottom of chat window, 
      stacked upward
    - Max 3 visible embeds at a time (oldest gets destroyed)
    - Embeds auto-destroy after 30 seconds or when chat clears
```

### Step 5: Patch Integration

**`ChatPatches.Chat_OnNewChatMessage_Postfix`:**
```csharp
var urls = ChatMediaDetector.ExtractImageUrls(text);
foreach (var (url, mediaType) in urls)
{
    ChatMediaEmbedManager.QueueEmbed(url, mediaType);
}
```

**`DiscordBotListener.FetchNewMessages`:**
```
When parsing Discord messages, also check msg["attachments"] array.
Each attachment has a "url" and "content_type" field.
Append direct media URLs to the message content before relaying,
OR relay them as separate embed requests.
```

---

## File Size / Safety Limits

| Limit | Value |
|-------|-------|
| Max download size | 5 MB |
| Max GIF frames | 100 |
| Max GIF dimensions | 400×400 px (downscale if larger) |
| Max visible embeds | 3 at a time |
| Embed lifetime | 60 seconds then fade out |
| Concurrent downloads | 2 max |

---

## Supported URL Patterns

| Pattern | Type | Resolution |
|---------|------|------------|
| `*.png`, `*.jpg`, `*.jpeg`, `*.webp` | Static | Direct download |
| `*.gif` | Animated | Direct download ? GifDecoder |
| `media.discordapp.net/*` | Static/GIF | Direct download (Discord CDN) |
| `cdn.discordapp.com/*` | Static/GIF | Direct download (Discord CDN) |
| `tenor.com/view/*` | Animated GIF | Resolve via Tenor oEmbed API ? get direct GIF URL |
| `giphy.com/gifs/*` | Animated GIF | Resolve via Giphy oEmbed ? get direct GIF URL |

### Tenor Resolution
```
GET https://tenor.com/oembed?url={tenor_url}
Response includes "url" field pointing to the embed page,
and can be parsed for the direct GIF.

Alternatively, extract the GIF ID from the Tenor URL and use:
GET https://tenor.googleapis.com/v2/posts?ids={id}&key={api_key}
? returns direct media URLs in multiple formats.

Simplest: use Tenor's direct media format:
https://media.tenor.com/images/{id}/tenor.gif
```

---

## Implementation Order

1. **`ChatMediaDetector.cs`** — URL regex extraction (no Unity dependencies, pure string parsing)
2. **`GifDecoder.cs`** — GIF89a byte parser ? frames + delays
3. **`ChatMediaEmbed.cs`** — Download + display + animate MonoBehaviour
4. **`ChatMediaEmbedManager.cs`** — Creation, positioning, lifecycle management
5. **Patch integration** — Wire into `ChatPatches` postfix
6. **Discord attachment support** — Extract attachment URLs in `DiscordBotListener`

---

## Dependencies

- `UnityWebRequestTexture` — already available (used by DiscordWebhookService)
- `Texture2D` / `RawImage` — standard Unity UI
- No external libraries needed — GIF decoder is pure C#
