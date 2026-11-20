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

## D-007 — Editor room mesh is a convex quad in room-local meters

- **Status**: Accepted
- **Decision**: `RoomGeometryData` stores four floor corners (`Vector2`, x = X, y = Z) and a wall height in meters. `RoomGeometryValidator` accepts only one convex quadrilateral and normalizes winding to counter-clockwise from above. `RoomMeshGenerator` then builds the floor with normals up and four walls with normals facing into the room. `RoomGeometryController` only applies that data in its local space.
- **Why**: The mesh must be testable in the Editor without ARCore, and later raycast hits must be converted into this model instead of being stored as session poses.
- **Note**: Concave polygons, more than four corners, JSON, and AR wiring are out of this slice. The 4 m × 3 m × 2.4 m example is a sample, not a measured room.

## D-008 — Feature branches and user-approved merges

- **Status**: Accepted
- **Decision**: Development uses a GitHub issue, a feature branch, and one draft pull request into `main`. Agents may commit and push that feature branch. They do not push `main`, force-push, or merge until the user explicitly approves the merge. Issue links use `Refs #` so a draft, and even a later merge, does not auto-close the issue.
- **Why**: Cursor and Codex work in separate worktrees. Direct `main` updates skip review and can overwrite the other agent's branch.
- **Note**: The procedure is `docs/DEVELOPMENT_WORKFLOW.md`.

## D-010 — Field-test room uses the first corner and one floor height

- **Status**: Accepted for the 2026-11-21 field test
- **Decision**: `ARFieldTest` keeps the Phase 0 AR session. Four `HorizontalUp` raycast hits are stored in tap order. `RoomLocalFrame` sets the room anchor at the first corner's XZ and one floor Y, with identity world rotation, parented to the XR Origin rather than the camera. Wall height stays 2.4 m. Edge lengths are horizontal meters for a tape comparison, not a survey.
- **Why**: The verified pose path and the Editor mesh builder stay separate. A mesh parented to the camera would slide when the phone moves.
- **Note**: A corner is the intersection of two vertical wall planes and one floor height. The touch point on a wall is not projected down to the floor. Floor height is locked only when one horizontal surface is clearly larger than the others. A large table can still win over a small visible floor patch, and the lowest plane is not chosen automatically. Estimated edge lengths are not survey measurements. Wall outlines show detected plane boundaries, not architectural edges.

## D-011 — Cluster wall fragments before closing a room

- **Status**: Accepted for `feat/ar-room-reconstruction-v2`
- **Decision**: A vertical `ARPlane` is a sample, not a wall. `WallClusterBuilder` merges samples whose normals agree within 12° after flipping opposite normals, whose plane offset is at most 0.18 m, and whose horizontal spans overlap or leave a gap of at most 1.2 m. `RoomReconstructionSolver` then searches observed clusters for one convex quad. A rectangle length check runs only when opposite walls are parallel and the directions are within 12° of orthogonal. Missing walls are not invented. Low-confidence or inconsistent candidates withhold the mesh. The user tap selects a cluster; `확정` is the only path into `RoomMeshGenerator`.
- **Why**: Device logs showed four corners built from eight plane ids. Independent pair intersections can close a mathematically valid polygon that does not match the room.
- **Assumptions, not measurements**: 12° and 0.18 m separate a closet face from the wall behind it. 1.2 m keeps a doorway gap on one wall. 0.45 confidence, 0.4 s observation, 0.45 m minimum edge, and 0.8 m² minimum area are starting gates. Change them on `WallClusterSettings` if a device log shows a real room rejected or a closet merged.

## D-009 — Do not renumber the roadmap to match the GeometrySandbox

- **Status**: Proposed
- **Decision**: Keep the existing phase numbers. Do not rename Phase 1 and Phase 2 in bulk in this change.
- **Why**: The Phase 1 heading says Geometry, but its bullets are still AR measurement. Phase 2 is 3D room generation and also contains the finished Editor `GeometrySandbox` bullet. Renumbering would rewrite history without a product decision.
- **Proposal**: Leave the numbers. The recommended later edit, after the user chooses, is to restore the Phase 1 heading to AR Measurement and keep the finished `GeometrySandbox` bullet under Phase 2. Do not apply that edit before the choice.
- **Note**: The next code task is `feat/ar-fieldtest`: four floor corners from AR raycast, then the existing room mesh, then a device APK. It does not add a phase number. The 2026-11-21–22 field test comes before more workflow or roadmap edits.

## Verified Facts (repo / local evidence)

| Fact | Evidence |
|------|----------|
| Unity project is `6000.6.9f1` with URP + Input System | `RoomTwinScanner/ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json` |
| AR Foundation / ARCore 6.6.2 are in the Unity manifest | `Packages/manifest.json` |
| Phase 0 probe and Editor room-geometry scripts are in `Assets/RoomTwin` | `ARPlaneProbe.unity`, `GeometrySandbox.unity` |
| Android min SDK is 29 in the committed player settings; product name `RoomTwinScanner` `0.1.0` | `ProjectSettings.asset` at `69c80fa`; bootstrap sets the same value |
| `origin/main` `native-ai/` is still a README stub; the Android project is on `origin/feat/native-ai` | `git ls-tree` of those refs on 2026-11-20 |
| AR Foundation samples cloned outside the repo | `C:\DigitalTwin\references\arfoundation-samples` |
| Samples Editor last opened `Markers` scene | samples `Library/LastSceneManagerSetup.txt` |

## Developer-verified (not CI)

These were reported by the developer. GitHub Actions did not run them.

| Fact | Evidence |
|------|----------|
| Galaxy S20+ shows the camera image, tracking, plane detection, raycast, and touch markers | Developer Phase 0 device report, 2026-11-20 |
| `ARInputManager` on the AR Session and `TrackedPoseDriver` bindings to `<HandheldARInputDevice>` were required for that tracking result | Same device report, after the earlier origin-pose failure |
| `GeometrySandbox` shows the floor and four walls, Inspector edits rebuild, a bad corner order warns, and the sample returns to 4 m × 3 m × 2.4 m | Developer Editor check, 2026-11-20 |
| Unity Edit Mode `RoomTwin.Geometry.Tests` — 7 passed, 0 failed, Test Runner time 0.016 s; Unity Console errors 0 | Developer Test Runner screenshot, 2026-11-20. Commit under test: `69c80fa` |

## CI-verified

| Fact | Evidence |
|------|----------|
| None yet | `native-ai` workflow is configured and has not been executed. Unity CI is not part of this repository. |

## Unverified Assumptions

| Assumption | Why unverified | Follow-up |
|------------|----------------|-----------|
| Target phones support Depth API well enough for MVP assist | No device matrix recorded in-repo | Capture device list + Depth support check |
| Horizontal/vertical plane scanning alone can seed a usable room | Samples present; no RoomTwin device test log | Phase 1 device trials |
| Manual correction can absorb plane error for demo rooms | Product choice, not measured | Prototype correction UX |
| Meshing / occlusion samples transfer cleanly into room reconstruction | Not evaluated on target devices | Optional post-MVP spike |
| LiteRT-LM + Gemma 4 E2B integration shape for Unity bridge | Owned by Codex; contracts not published here | Wait for Codex interface doc |
| Local JSON (or similar) is sufficient persistence | Format not chosen yet | Decide in Phase 4 |
| S20+ Phase 0 build ran on Vulkan | Graphics API is Automatic; runtime API not logged | Check `adb logcat` for the selected API |
| `native-ai` CI passes on GitHub-hosted runners | Workflow file added; Actions has not run | Open a PR that changes `native-ai/**` after this workflow is on `main` |

## Open Questions

1. Persistence format (JSON vs ScriptableObject vs custom binary)?
2. First furniture catalog size and collision rules?
3. Minimum Android devices for Phase 1 acceptance?
