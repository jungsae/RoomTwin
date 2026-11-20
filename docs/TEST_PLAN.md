# Test Plan

## Principles

- Separate **Editor / unit**, **device AR**, and **native-ai** results.
- Do not mark a feature verified unless the environment is named.
- AI / VLM output is never an acceptance source for measurements.

## Environments

| Environment | Purpose |
|-------------|---------|
| Unity Editor | Mock room builder, save/load, furniture logic |
| Android device (ARCore) | Session, planes, tracking, on-device UX |
| Codex / `native-ai` harness | Kotlin AI module only (out of Cursor scope) |

## MVP Test Matrix

| Capability | Editor | Device | Notes |
|------------|--------|--------|-------|
| AR package boot / session | smoke only | developer-verified on Galaxy S20+ | not a GitHub Actions result |
| Measurement capture | mock inputs | required | treat planes/depth as uncertain |
| 3D room generation | developer-verified for one convex quad | not run in AR | `GeometrySandbox` only |
| Manual correction | required | required | edit then remesh |
| Save / load | required | required | round-trip room-local data |
| Furniture placement | required | required | pose persists with room |

## Acceptance for Demo

1. Scan or mock a single room.
2. Correct at least one dimension.
3. Place at least one furniture preset.
4. Kill app / leave play mode, reload, and restore room + furniture.
5. Report any failed tracking or large dimension error as known limitation.

## Editor Room Geometry

1. Open `Assets/RoomTwin/Scenes/GeometrySandbox.unity`. Do not enter Play mode yet. A 4 m × 3 m floor and 2.4 m walls should appear, with corner labels 1–4.
2. On the `Room` object, change one corner or the wall height. The mesh rebuilds. The floor stays on y = 0 and the walls meet the new corners.
3. Enter a crossed corner list, such as `(0,0), (4,3), (4,0), (0,3)`. The mesh clears and the Inspector shows a convex-order error.
4. Run Edit Mode tests in Window > General > Test Runner. The `RoomTwin.Geometry.Tests` assembly covers winding, inward wall normals, clockwise input, and invalid corners.
5. This scene has no AR Session. Phone and ARCore are not required.

Developer result on 2026-11-20, Unity `6000.6.9f1`, commit `69c80fa`: the floor and four walls were visible; corner and height edits rebuilt; a bad corner order warned; the sample was restored to 4 m × 3 m × 2.4 m. Edit Mode `RoomTwin.Geometry.Tests` reported 7 passed, 0 failed, 0.016 s. The Console showed 0 errors. This was a local Test Runner run, not CI.

## CI strategy

- `native-ai` pull requests that change `native-ai/**` use `.github/workflows/native-ai-ci.yml`: JDK 17, Gradle 9.3.1, Android SDK 36, `:app:testDebugUnitTest`, `:app:assembleDebug`, and `:app:lintDebug`. No model weight is downloaded. A green run does not cover Galaxy S20+ inference.
- As of 2026-11-20 that workflow has not been executed. `origin/main` does not contain the Gradle project. `origin/feat/native-ai` does, and it has no Gradle wrapper and no `app/src/test` tree yet. The local Codex worktree is ahead of that remote and was not modified here.
- Unity CI is intentionally absent. The Editor license blocks batch mode, and agents must not start Unity. Geometry and AR Editor tests stay in Test Runner on a developer machine.

## Phase 0 Touch Marker Alignment (device)

1. RAYCAST mode: tap a textured floor or desk, hold the phone still, and read the HUD. The cyan touch cross and the magenta hit reprojection should overlap, with an error of 20 px or less.
2. Move the phone sideways. If the yellow live-marker square drifts away from the real surface point, the plane depth or pose is wrong (ARCore), not the touch mapping.
3. TEST mode: each tap places a marker 1 m ahead, which should project to the screen center. No raycast is run.
4. Collect the `[RoomTwin][Phase0][Touch]` lines with `adb logcat -s Unity` for any failing case.

## Untested Until Explicitly Run

- Target-phone Depth API support
- Vertical plane quality on blank walls
- Long-session tracking drift
- Performance on mid-range Android devices
- Any `native-ai` on-device inference path (CI does not replace it)
- GitHub Actions execution of `.github/workflows/native-ai-ci.yml`

## Reporting Template

```text
Tested: <environment> — <what passed>
Untested: <what was not run>
Risks: <known assumptions>
```
