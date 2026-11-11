# Grilled Squid 1.0.2 (PTVFOOD)

Windows x64 game. Launcher ID / signed ticket audience: `ptvfood`. Executable: `PTVFOOD/PTVFOOD.exe`.

## Six skills

1. Grill Squid — fixed configured number of squid.
2. Make Sauce — fixed configured number of cups.
3. Prepare Sets — fixed configured number of squid-and-sauce sets.
4. Customer Orders — fixed configured number of ordered sets.
5. Complete All Orders — completes every order currently waiting, with a grilled-squid icon.
6. Random Help/Prank — randomly chooses a help or prank action; its quantity is the configured single number.

Each skill has one gift binding. No levels or min–max ranges appear in the HUD or settings.
Gift batches multiply the first four fixed quantities; every random spin is retained in the counted queue.
Skill groups remain draggable, resizable, and saved separately for landscape and portrait.

## Existing settings

`PTVFOOD.Gifts3` stores the six-skill format. When absent, `PTVFOOD.Gifts2` is read and migrated
from old indices 0, 4, 8, 12, 16 and 17. The former minimum becomes the fixed amount. The original
20-slot preferences remain untouched for recovery. Migration reads at startup; saving writes only
the new key. TikTok username, language and other settings are retained.

## Build and access

Use the existing PTVFOOD models, scene, fonts and MintUI assets. Run `PTVFOODBuild.Prepare()`
and build Windows x64, Mono, 1.0.2, with the version-gate scene first and SquidStall second.
Retail builds reject `PTV_RELEASE_QA`. Ship the complete production TikTokBridge with runtime/node.exe
and node_modules beside the game executable; exclude logs, tests and `_DoNotShip` folders.

Fresh installs default to English; Thai switches all labels and the shop sign. Existing per-game
sales remain THB 180 / US$7 for 30 days. The signed play ticket and mandatory version check remain enforced.
