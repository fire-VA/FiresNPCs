# ServerInfo — Marketplace config schema

ServerInfo backs the "Info" NPC — a scrollable rich-text + image page (welcome / rules / description). It does **not** model structured buttons or link rows; everything is free-form rich text plus inline images, parsed into an ordered queue of Text/Image blocks.

## Config location
- Folder: `Configs/ServerInfos/*.cfg` (recursive merge — `SearchOption.AllDirectories`) → `Market_Paths.ServerInfoProfilesFolder`.
- Autoload watch key / on-change handler (from `[Market_Autoload(... new[]{"SI"}, new[]{"OnServerInfoProfileChange"})]`):
  - `"SI"` → `OnServerInfoProfileChange` → `ReadServerInfoProfiles()` (clears + full re-read of the folder, then `SyncedServerInfoData.Update()`).
  - `OnInit` → same `ReadServerInfoProfiles()`.

## Section header
- `[name]` → section key = `name` stripped of `[`, `]`, all spaces, then **`.ToLower()`**. Default key before any header = `"default"`.
- **No `[name=bool]` flag.** A `=` would just be part of the key.
- A header **closes the previous section**: when a `[` line is hit and the accumulated text buffer `str1` is non-empty, the buffer is flushed as a final `Text` block, the section's `infoQueue` is stored into `SyncedServerInfoData[key]` (overwriting), then the buffer/collection reset for the new key. **Last-write-wins per key** across the whole folder (a later file with the same `[key]` overwrites the earlier one — it does NOT append).
- The final section is flushed after the loop (if `str1` non-empty).

## Data-line grammar
Unlike the other modules, ServerInfo does **not** `.Replace(" ", "")` and does **not** CSV-split. Lines are accumulated as multi-line rich text. The only line classes:

1. **Comment** — starts with `#` → skipped. (Note: blank/whitespace lines are NOT skipped here — there is no `IsNullOrWhiteSpace` guard, only the `#` check. A blank line is appended as an empty line `"\n"` into the text buffer, preserving vertical spacing.)
2. **Header** `[...]` — section control (see above).
3. **Image line** — any line **containing** the literal substring `<image=`. Parsed as:
   - Split the whole line on `"<image="` → `[before, rest]`.
   - Split `rest` on `">"` → `[url, after]`.
   - `url` (`strArray2[0]`) = the image source string.
   - Flush `currentBuffer + before` as a **Text** block, then add an **Image** block whose `Text` = `url`, then seed the buffer with `after + "\n"`.
   - So one source line `pre<image=URL>post` becomes: Text(`pre`), Image(`URL`), and `post` begins the next text run. Only the **first** `<image=` per line is handled this way (the split takes index 0/1); a second `<image=` on the same line would be left embedded in `after` text and not parsed.
4. **Plain text** — anything else → appended to the buffer as `str1 + line + "\n"`.

Text blocks are stored with trailing `\n` trimmed only at section-flush (`str1.TrimEnd('\n')`); interior newlines are preserved. Valheim rich-text tags (`<color>`, `<b>`, `<size>`, `<link>`, etc.) are passed through verbatim inside Text blocks — **links/buttons, if any, are just rich-text `<link>`/`<color>` markup inside the text, not a separate config field.**

```cfg
# Welcome page (lowercased key "welcome")
[welcome]
<size=30><color=orange>Welcome to the server!</color></size>

Rules:
1. Be nice.
2. No griefing.

Here is our logo: <image=https://example.com/logo.png>and we hope you enjoy.
```
That section's `infoQueue` becomes, in order: Text("…Welcome…\n\nRules:\n1…\n2…\nHere is our logo: "), Image("https://example.com/logo.png"), Text("and we hope you enjoy.").

## Domain object

### `ServerInfo_DataTypes.ServerInfoQueue : ISerializableParameter`
- `List<ServerInfoQueue.Info> infoQueue`.
- Nested `ServerInfoQueue.Info`: `string Text; InfoType Type; (private Sprite tex)` with `SetSprite/GetSprite`. `enum InfoType { Text = 0, Image = 1 }`.
- For a **Text** block, `Text` holds the rich-text run. For an **Image** block, `Text` holds the image source string (URL / cached-image key); the rendered `Sprite` is resolved client-side and stashed via `SetSprite`.
- ZPackage wire order: `infoQueue.Count`, then per item `(int)Type, Text`.

### Synced container
`SyncedServerInfoData : CustomSyncedValue<Dictionary<string, ServerInfoQueue>>` key `"infoData"` — section-key → ordered queue.

## Persistence
- **Static config only.** ServerSync / `CustomSyncedValue` (`infoData`), rebuilt from the `.cfg` files on init and on file change. No LiteDB, no player customData, no per-player state — it is purely server-authored display content pushed to clients.
- Images referenced by `<image=...>` are resolved client-side (cached-image system / URL fetch into `Marketplace_CachedImages/`), not stored in config.

## NPC linkage
- `NPCType.Info` (enum value `Info`). On interact, `OpenUIForType` → `ServerInfo_UI.Show(profile, npcName)`.
- The NPC ZDO `Profile` is `.ToLower()`-ed before the call, matching the lowercased section keys. `ServerInfo_UI.Show` bails if `SyncedServerInfoData` lacks the profile key. If `npcName` is empty the panel title falls back to the localized `$mpasn_Info`.

## Gotchas / risks for the parser
- **No space-stripping, no CSV.** This is a line-accumulating rich-text parser. Do NOT apply the shared `.Replace(" ","")` / comma-split convention here — it would corrupt the text.
- **Blank lines are significant** — they are preserved as empty lines in the text (only `#` lines are dropped). There is no whitespace-skip guard, so a fully blank first line before any header still appends to the `"default"` section's buffer.
- **Last-write-wins per section key** across files (overwrite), unlike Transmog (append) and Buffer profiles (first-write-wins). Recursive merge with duplicate `[key]`s keeps the last file processed.
- `<image=...>` parsing is naive: requires a closing `>`; assumes exactly one image per line cleanly splitting into before/url/after. A malformed tag (no `>`) will throw `IndexOutOfRangeException` on `strArray2[1]` — and there is **no per-line try/catch** in `ProcessServerInfoProfiles`, so a bad image line aborts the whole file's parse (the outer `ReadServerInfoProfiles` has no catch either; it would bubble to the autoload caller). Validate `<image=URL>` well-formedness before splitting.
- The image `url` and the surrounding text ordering matters — emit Text-before-Image-then-Text exactly, or the page renders out of order.
- A section with only image lines and no trailing text still flushes correctly because each image line pushes Text(before)+Image; but a section whose buffer is empty at a header boundary is NOT stored (the `if (!IsNullOrEmpty(str1))` guard) — an empty section produces no entry.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
