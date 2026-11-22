# TABS Live Arena 1.6.0

Windows arena integration for Totally Accurate Battle Simulator. The catalog entry is `tabslive`, with free access and no play ticket or purchase price.

## Languages and window layout

Mod labels use `TabsCrispText`, with a Harmony adjustment limited to that Text subclass. Font pixel density includes scaled parent panels and 2x sampling; Text's own generation and reciprocal mesh conversion remain paired. Changing display scale invalidates glyph geometry. Centering offsets snap to screen pixels. Portrait labels are larger, and opening an editing menu temporarily hides the hero tray. Painted art/frame textures have mipmaps for downsampling.

English is the default for fresh and existing configurations that have no language preference. Use **EN / ไทย** in the LIVE panel or gift settings to switch immediately. `TikTokArena.json` stores `language: "en"` or `"th"`. Locale source files are `Locales/en.json` and `Locales/th.json`; the build embeds both and the player also supports files under `TikTokUI/Locales/`. Viewer identities and gift names remain unchanged.

The canvas uses a fit-safe reference size for landscape and portrait. The scoreboard anchors to the top, the hero tray anchors to the bottom, and the control panel fits within the available canvas. Portrait mode has two faction rows, a 4 + 3 hero grid, taller gift settings and a three-column gift picker. **TikTok 9:16** chooses an exact 9:16 windowed client area that fits the current display, with title-bar space reserved.

- TikTok LIVE chat `1` / `2` selects red / blue. Choices reset on every new round/map and arena clear. Rewards wait until a viewer chooses; no previous-round team files are read and first gifts no longer assign randomly.
- Square profile photos without names confirm choices in scrollable side lists. A viewer has one entry; switching sides moves it. Round/map resets clear the lists.
- Like packets retain per-viewer batch deltas, handle zero-default fallback fields and merge safely under socket backpressure. F8 shows round likes received and like rewards. `TabsLikeCounter` is the production arithmetic helper, with per-viewer/hero remainders cleared each round. Never infer personal taps from room-wide totals.
- Seven units from the selected native faction are mapped to gifts. Both teams share the roster. Gift matching supports IDs, names and pictures. Configurable likes, follows and Heart Me triggers remain available.
- Painted character cards, gift badges, plain black profile borders, living-unit counts and round score HUD. Native sword button starts battle.
- The original native game camera is retained. The experimental automatic framing component has been removed.
- After the native victory effect, the round returns to placement on the current map and clears restored units, survivors and queued rewards. The next battle starts only when the sword button is pressed.
- WIN totals last for the current game session only. Restarting the game starts red/blue wins at zero and round one; old score files are not loaded.

The mod leaves the native game camera unchanged. Build with `build.ps1 -GameDir <TABS folder>` after installing BepInEx 5 for that player. The compiler path in the script targets the local Unity installation and can be adjusted for another machine.

Release archive includes the Windows player, BepInEx integration, bridge dependencies, bundled Node runtime, gift catalog and painted UI assets. Developer team history, scores, streamer account, old DLL backups, portrait cache and debug build files are omitted. Connect the intended TikTok LIVE account in the in-game menu after installation.

Validation: mod compilation against the installed game assemblies, launcher self-tests, archive structure and fresh-account configuration checks. In-game testing was not performed at the user's request.
