# Mandatory game updates — PTVCar and PTVvs

Both Unity projects now contain `Assets/PTVUpdate/PTVVersionGate.cs` and `Assets/PTVUpdate/Editor/PTVMandatoryBuild.cs`.

The new Windows builds are **1.0.1**. They begin with the empty `PTVVersionGate` scene, and load the actual gameplay scene only after the HTTPS manifest reports the same version as `Application.version`. Missing internet, malformed responses and version mismatches block entry. Retry, download-launcher and quit buttons remain available.

The gate checks again every 60 seconds and when the game regains focus. If verification then fails, the overlay blocks input, simulation pauses, and the gameplay scene is unloaded back to the bootstrap scene. Verification is online; no offline grace period is enabled.

Game identity and update URLs:

| Project | Game | Manifest |
| --- | --- | --- |
| PTVCar | PTVCar | `release-manifests/ptvcar.json` |
| SuMo | PTVvs | `release-manifests/ptvvs.json` |

## Future releases

1. Increment the version in `PTVMandatoryBuild.cs` (currently `PlayerSettings.bundleVersion="1.0.1"`) and the build output folder name.
2. Build using `PTVMandatoryBuild.Run()` in the Unity Editor, or a build command that retains `Assets/Scenes/PTVVersionGate.unity` as the first scene. The build validator rejects a scene list that omits the gate at index zero.
3. Include the game's TikTokBridge, licenses and relevant supporting files when packing the entire build folder.
4. Upload the complete ZIP to GitHub Releases first.
5. Generate the manifest with the new version, actual ZIP size, SHA-256, HTTPS asset URL and executable path.
6. Update the stable JSON manifest on the main branch **after the assets are published**. The launcher and game use the same stable version URL.

Keep the `sumo.json` alias synchronized with `ptvvs.json` while old launchers still reference it. The launcher's internal game ID remains `sumo` to retain installed-state and favorites compatibility.

## Limits

EXEs already distributed before the gate existed cannot be revoked retroactively. This is client-side version enforcement; someone who modifies the binary can bypass client code. A server-authoritative account/session system is needed to deny service to modified clients. The game does not transmit user identities for these checks; it reads only the public release manifest.

## Validation

`PTVMandatoryBuild.TestPolicy()` checks allowed/current, outdated, offline, malformed and normalized numeric version cases. The new EXEs are also tested against the real update endpoint before and after publication.

PTVvs uses TMP Essential Resources for the version-screen text; these resources were imported non-interactively into the SuMo project.
