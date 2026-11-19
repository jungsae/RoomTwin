# Decisions

Format: decision, status, context. Keep facts and assumptions separate.

## D-001 — Split rules and design docs

- **Status**: Accepted
- **Decision**: Shared agent rules stay in `AGENTS.md`. Cursor execution rules live in `.cursor/rules/*.mdc`. Product design lives under `docs/`.
- **Why**: Avoid duplicating long design text into always-on agent context.

## D-002 — MVP without on-device AI

- **Status**: Accepted
- **Decision**: MVP = AR measure, 3D room, manual correction, save/load, basic furniture. Kotlin AI stays in `native-ai/` (Codex) and is not required for MVP.
- **Why**: Deliver a reliable spatial loop before adding model/runtime complexity.

## D-003 — Session space vs room-local space

- **Status**: Accepted
- **Decision**: Persist only room-local meters. AR session poses are transient.
- **Why**: Tracking origin can shift; saved rooms must reload without the old session.

## D-004 — Planes are not walls

- **Status**: Accepted
- **Decision**: AR plane boundaries are measurement candidates. Final walls come from the editable room model after user correction.
- **Why**: ARCore planes are feature-point based and incomplete on low-texture surfaces.

## D-005 — Phase 0 plane rendering off for camera-background diagnosis

- **Status**: Temporary (diagnostic)
- **Decision**: `ArPlaneVisual.prefab` keeps `ARPlane` but disables `ARPlaneMeshVisualizer` and `MeshRenderer`. `Mobile_Renderer` (Android quality level → `Mobile_RPAsset`) gets `ARBackgroundRendererFeature` and `ARCommandBufferSupportRendererFeature`.
- **Why**: Galaxy S20+ build showed tracking + 27 planes but no camera image, full-screen blue planes, black triangles, and severe lag. `Mobile_Renderer` had no renderer features, which AR Foundation URP setup requires; Android graphics API is Automatic with Vulkan listed, and ARCore docs require the command-buffer feature for Vulkan.
- **Note**: Re-running `RoomTwin/Phase0/Bootstrap AR Scene And XR` regenerates the plane prefab and scene and would undo the prefab change.

## D-006 — Touch/marker mismatch is diagnosed by hit reprojection

- **Status**: Temporary (diagnostic)
- **Decision**: In raycast mode, the hit pose is projected back to the screen with `Camera.WorldToScreenPoint` and compared with the touch pixel. An error at or below 20 px means the coordinate path is correct, so a misplaced marker comes from the ARCore plane pose or depth. A larger error means a code or coordinate issue. Test mode skips the raycast and places the marker 1 m ahead, which should project to the screen center.
- **Why**: A raycast hit lies on the ray through the touched pixel by construction, so a large reprojection error cannot come from bad plane estimation alone.
- **Note**: AR Foundation normalizes the raycast point by `Screen.width/height`, while the reprojection uses the camera's pixel size. The HUD shows both values so a mismatch is visible. The 20 px tolerance and the floor-height range (0.2–2.5 m) are assumed values that have not been measured.

## Verified Facts (repo / local evidence)

| Fact | Evidence |
|------|----------|
| Unity project is `6000.6.9f1` with URP + Input System | `RoomTwinScanner/ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json` |
| AR Foundation / ARCore packages are not in the Unity manifest yet | `Packages/manifest.json` |
| No RoomTwin feature gameplay scripts yet | `Assets/` has SampleScene + TutorialInfo only |
| Android min SDK is 26; product name `RoomTwinScanner` `0.1.0` | `ProjectSettings.asset` |
| `native-ai/` exists as a separate module stub; Codex-owned | repo layout + root `AGENTS.md` |
| AR Foundation samples cloned outside the repo | `C:\DigitalTwin\references\arfoundation-samples` |
| Samples Editor last opened `Markers` scene | samples `Library/LastSceneManagerSetup.txt` |

## Unverified Assumptions

| Assumption | Why unverified | Follow-up |
|------------|----------------|-----------|
| Target phones support Depth API well enough for MVP assist | No device matrix recorded in-repo | Capture device list + Depth support check |
| Horizontal/vertical plane scanning alone can seed a usable room | Samples present; no RoomTwin device test log | Phase 1 device trials |
| Manual correction can absorb plane error for demo rooms | Product choice, not measured | Prototype correction UX |
| Meshing / occlusion samples transfer cleanly into room reconstruction | Not evaluated on target devices | Optional post-MVP spike |
| LiteRT-LM + Gemma 4 E2B integration shape for Unity bridge | Owned by Codex; contracts not published here | Wait for Codex interface doc |
| Local JSON (or similar) is sufficient persistence | Format not chosen yet | Decide in Phase 4 |
| Missing URP AR renderer features caused the S20+ black/blue screen and lag | Fix applied in assets, not yet re-run on device | Rebuild APK, compare with D-005 symptoms |
| Binding `TrackedPoseDriver` to `<HandheldARInputDevice>/devicePosition` and `deviceRotation` fixes the camera stuck at the origin (scene had empty bindings; `Reset()` creates unbound actions) | Scene and bootstrap fixed; not re-run on device | Rebuild, then check that `camPos`/`camRot` change in `[RayCompare]` and that the screen-path reprojection error drops |
| `ARInputManager` on the AR Session is required to start `XRInputSubsystem` for that pose | Bootstrap now adds one if missing; device not re-run | Confirm the component is on AR Session, then repeat the camera-pose check |
| S20+ Phase 0 build ran on Vulkan | Graphics API is Automatic; runtime API not logged | Check `adb logcat` for the selected API |

## Open Questions

1. Persistence format (JSON vs ScriptableObject vs custom binary)?
2. First furniture catalog size and collision rules?
3. Minimum Android devices for Phase 1 acceptance?
