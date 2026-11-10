# Grilled Squid 1.0.0 (PTVFOOD)

Windows x64 Unity game. Launcher ID and signed ticket audience: `ptvfood`.
Archive root: `PTVFOOD/`; executable: `PTVFOOD/PTVFOOD.exe`.

Fresh installations start in English. The language switch changes the home screen,
settings, gift picker, gameplay and shop sign between English and Thai. Food icons
replace gift icons on the home screen; gift mappings remain available in settings.

## Access and sales

The existing per-game system sells `g_ptvfood` for 30 days at THB 180 / USD 7.
Migration `20261111000000_ptvfood.sql` registers the plan. Initial agent pricing follows
the existing 75% policy (THB 135 / USD 5.25), editable through Admin.
Existing PromptPay, PayPal, wallet purchases, agent codes, renewal and signed tickets
operate through the same server functions as other games. No signing keys ship.

`PTVLicense` verifies the production RSA signature, game audience, short ticket lifetime
and game entitlement, then checks entitlement expiry during a running session.
`PTVVersionGate` requires the current release-manifest version before loading gameplay.
Editor gameplay remains available for development. Retail builds contain no QA bypass.

## Build

Copy this folder's C# scripts into the existing PTVFOOD project, preserving subfolders.
Keep the project's existing models, scene, fonts, mint UI sprites and reusable prefabs.
Run `PTVFOODBuild.Prepare()` in the Editor. Build Windows x64 with Mono, version 1.0.0,
with `PTVVersionGate` first and `SquidStall` second. Do not set `PTV_RELEASE_QA` in retail.
The build guard preserves runtime shader variants through specific material references
and enables mesh read access needed by the animated mortar.

Package `TikTokBridge/` beside the executable with production .mjs files,
package metadata, node_modules and runtime/node.exe. Exclude test files, local secrets,
logs, build debug folders and all `_DoNotShip` directories.

## Artwork

`assets/ptvfood.png` was generated with the built-in image_gen tool using an actual
Unity gameplay screenshot as reference. The cover preserves the vendor, orange table,
rectangular ice box, charcoal grill, mortar and ingredient layout. NEW uses the
Launcher's separate existing gold badge; the PTVUP badge is removed.
