# PTVUP 1.0.3

Windows x64 Unity game. Product name: PTVUP. Launcher ID and ticket audience: ptvup.

The complete package is published as PTVUP.v1.0.3.zip in the ptvup-v1.0.3 release. ZIP root: PTVUP/. Entry point: PTVUP/PTVUP.exe. Include UnityPlayer.dll, PTVUP_Data, MonoBleedingEdge and the entire production TikTokBridge with runtime/node.exe and node_modules.

## VIP verification

Catalog requires access=vip and playTicket=true. The existing PTVgame Launcher obtains a signed play-ticket for ptvup and passes --ptv-ticket. No new server deployment is needed: play_ticket_claims accepts validated game IDs and checks the signed-in account's VIP/admin status. The embedded public key matches the live play-v1 endpoint. Only the public RSA key is distributed.

PTVLicense.Enforce=true in standalone builds; Unity Editor remains available for development. Validate RS256 signature, key ID, audience, positive PTV ID, nonce, 600-second lifetime and 60-second clock tolerance. Non-admins need a future VIP expiry. Verified sessions survive round/scene reloads; VIP expiry is checked every minute and blocks gameplay when it ends. Missing, forged, malformed, wrong-game and expired tickets are blocked.

15 cryptographic policy cases passed in Unity, including a generated test signer and rejection of that signer by the production key. The production signing key is never exported.

## Branding

Built-in image_gen was used with fresh Unity captures of the real zombie and rooftop beanstalk scene. Logo: assets/ptvup-logo.png. Cover: assets/ptvup.png. Artwork is illustrated promotional art based on gameplay.

Logo prompt:

Use case: logo-brand. Asset type: game title logo on a genuine transparent background. Exact only text: PTVUP (one word, five Latin uppercase letters, spelled P T V U P). Design an expressive premium cartoon game logo for the referenced actual game: a friendly green zombie climbing a huge twisting lime-green beanstalk from a colorful rooftop city up to a pink brain in the clouds. Use chunky hand-drawn dimensional ivory/golden cream letters with thick dark forest-green ink outlines, little chips and warm shadows, vines wrapping behind and around the lettering, 3 or 4 bright green leaves, a tiny pink brain as a summit reward, subtle upward arrow shape incorporated into the UP. Compact horizontal title plaque silhouette, about 3:1 wide, generous clear padding, full logo visible with no clipping. Match the comic garden UI from image 2, black imperfect ink contour and halftone accents, but crafted legible letterforms. Keep main letters dominant at small menu size. No rectangular neon strip, no other text, no URL, no subtitle, no watermark, no background or scenery. Image 1 is the game's exact zombie design as style/context; image 2 is the game/menu context. Do not put the whole zombie in the logo.

Cover prompt:

Use case: stylized-concept. Asset type: landscape 16:9 polished game cover / launcher thumbnail. Exact title text at top left: PTVUP, large and highly legible, five uppercase Latin letters P T V U P, dimensional ivory/gold cartoon lettering with dark green ink outline and little curling green vines, matching a playful garden comic game. References: image 1 is the exact main zombie model, preserve his identity (lime green skin, sleepy friendly half closed eyes, swept brown hair, two big separated front teeth, torn orange-brown jacket over pale cream shirt, purple torn shorts, chunky brown boots); image 2 is the actual beanstalk/rooftop city and comic visual theme. Composition: dynamic friendly zombie prominently on the right half, three-quarter face visible while climbing/hugging a massive vivid green twisting beanstalk that rises diagonally toward soft white clouds and a glowing pink brain reward at upper right. Far below left is a sunny colorful low-poly rooftop lawn and small town like the game. A small mischievous red balloon and yellow rubber duck near the vine hint at skills, subtle upward luminous green trails and loose leaves. Bright clear cyan sky, warm sunlight, joyful comedic adventure, polished cartoon 3D illustration with ink contour accents. Show vertical climbing scale. Leave title clean readable negative space at upper left; no login menu, no HUD, no buttons, no progress bar, no extra text or logos. Full bleed landscape 16:9 cover.

## 1.0.2: verified current player build

The previous player crashed in BonkChoySkill.Initialize because URP Unlit was stripped. ClimbGame.BuildUI stopped after creating the legacy duck button, before it hid old buttons or built the new two-column HUD. This was a player-only startup failure, not simply a version-number mismatch.

PTVUPBuildGuard retains the runtime-only shader and enables read/write on meshes used for runtime beanstalk colliders and balloon mesh extraction. It runs before every build. PTVUPPlayerAudit reports startup readiness (8 skills, 2 columns, 8 tiles, no active legacy duck UI).

A separate local development QA build with PTV_RELEASE_QA exercised all eight actual skill paths and both screen orientations. This QA flag only bypasses the ticket gate when DEVELOPMENT_BUILD is also defined. The distributed clean release has neither flag; its compiled Boot method calls the production ticket check and its audit contains no QA automation. Standalone startup was verified with ready=True, shaderUnlit=True and the expected no-ticket lock, without startup exceptions or collision-mesh errors. Source fingerprints are in build-info.json.

Broken 1.0.0 and 1.0.1 releases are retired only after the 1.0.2 archive is verified and the stable manifest points to it.

## 1.0.3: zoom, skill distances, meter size

- Camera zoom: mouse wheel or +/- (0 resets), 0.35x–3x, saved in PlayerPrefs (PVZUP.CamZoom).
- Settings › ระยะสกิล: metres per use for each of the 8 skills (1–5000 m), saved as PVZUP.SkillMetres.N and applied immediately; labels in the gift tab and notices follow the new values.
- Settings › ขนาดสกิล: climb-meter size slider 40–300% (PVZUP.MeterScale), previewed live, kept on save, restored on cancel.

Clean build (CleanBuildCache, LZ4HC, no development/QA defines). Standalone startup: `[PTVLicense] locked: no-ticket` and `[PTVUP Player] version=1.0.3 ready=True skills=8 columns=2 tiles=8 legacyActive=False shaderUnlit=True`. Package validated for the required entries and PTVUP/ root before upload. 1.0.2 is kept (not retired).
