# TABS Live Arena 1.4.0

Windows arena integration for Totally Accurate Battle Simulator. The catalog entry is `tabslive`, with free access and no play ticket or purchase price.

- TikTok LIVE chat `1` / `2` selects red / blue. A first gift assigns an unassigned viewer randomly and persists the team; later chat can override it.
- Seven units from the selected native faction are mapped to gifts. Both teams share the roster. Gift matching supports IDs, names and pictures. Configurable likes, follows and Heart Me triggers remain available.
- Painted character cards, gift badges, plain black profile borders, living-unit counts and round score HUD. Native sword button starts battle.
- The original native game camera is retained. The experimental automatic framing component has been removed.

The mod leaves the native game camera unchanged. Build with `build.ps1 -GameDir <TABS folder>` after installing BepInEx 5 for that player. The compiler path in the script targets the local Unity installation and can be adjusted for another machine.

Release archive includes the Windows player, BepInEx integration, bridge dependencies, bundled Node runtime, gift catalog and painted UI assets. Developer team history, scores, streamer account, old DLL backups, portrait cache and debug build files are omitted. Connect the intended TikTok LIVE account in the in-game menu after installation.

Validation: mod compilation against the installed game assemblies, launcher self-tests, archive structure and fresh-account configuration checks. In-game testing was not performed at the user's request.
