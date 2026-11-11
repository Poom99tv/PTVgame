# Grilled Squid 1.0.3 (PTVFOOD)

Windows x64. Launcher ID / signed ticket audience: ptvfood. Executable: PTVFOOD/PTVFOOD.exe.

## Thirteen skills

- Slots 1–4: helper levels 1–4. Each makes the configured number of complete sets: N grilled squid AND N sauce cups.
- Slot 5: completes all orders waiting at that moment, with no quantity setting.
- Slots 6–10: prank levels 1–5. Each adds the configured number of order sets.
- Slots 11–13: Random Help/Prank, Small / Medium / Large. Each randomly chooses combined help or additional order sets; its configured single quantity determines the result size.

Every non-random skill uses the grilled-squid artwork. Only random skills use the wheel.
The HUD keeps levels in order. Settings has three pages: Helpers + Complete All, Pranks, Random.
Quantities are single integers. No min–max ranges appear. Gift batches preserve every random spin.
HUD groups remain draggable, resizable, with separate portrait and landscape positions.

## Settings migration

PTVFOOD.Gifts4 stores the 13-skill format. Startup can read older Gifts2 (20 slots) or interim Gifts3 (6 slots).
The old keys remain untouched. Old helper, complete-all, prank and random bindings are retained by level;
the former minimum becomes the fixed quantity. The new fifth prank receives an unused default gift.
For the 20-slot format, the previous four grill bindings become the four combined-set helpers.
TikTok username and language preferences are retained. Fresh installations default to English.

## Build

Use the existing project models, scene, fonts and MintUI resources. Run PTVFOODBuild.Prepare(); build
Windows x64 / Mono / 1.0.3, with the version-gate scene first and SquidStall second.
Copy Branding/AppIcon.png to Assets/PTVFOOD/Branding/AppIcon.png before Prepare;
the Windows player uses this supplied application icon.
Retail builds reject PTV_RELEASE_QA. Package the complete production TikTokBridge with runtime/node.exe
and node_modules beside the executable, excluding test/log/debug and _DoNotShip files.
Signed per-game play tickets, mandatory version checks, and the 30-day THB 180 / US$7 plan remain in use.

## Escape key

Escape closes the frontmost HUD settings, gift picker, or gift settings first.
During gameplay it returns to the TikTok connection screen, pausing the current round.
Existing stock, orders, username, LIVE connection, and queued gifts are preserved.
Play resumes the same round and processes queued gifts. Escape on the connection screen stays there.
