# F1 2026 Game — Master Work Plan, Verified State & Continuation Guide

> **Last Verified Build:** 2026-11-26 | **Unity Exit Code:** `0` (`CircuitBuilder.BuildFinalGameAll`) | **All Tests Passed (Including Baku & Monaco Pit Exit `barrierHits=0`)**

---

## 1. Environment & Project Roots

- **Blender Assets Workspace:** `E:\My Game assets\f1`
  - Car & Tyres: `E:\My Game assets\f1\formulla 1 & tyres\F1_2026_Game.blend` (+ `textures\`)
  - Pit Crew & Garages: `E:\My Game assets\f1\pit_crew\`
  - Circuits (`monaco gp`, `baku gp`, `monza gp`, `spa gp`, `suzuka gp`): `E:\My Game assets\f1\maps\<circuit>\`
  - Pipeline Skill & Archived Scripts: `E:\My Game assets\f1\skill file\` (scripts archived in `scripts\unity_f1_overhaul\`)
- **Unity Project (`Unity 6000.3.26f1` URP):** `D:\my Gits\game-dev-\game-dev-`
  - Original scenes in `Assets/Scenes/*.unity` remain **untouched**
  - Final production scenes live in `Assets/Scenes/Final/`:
    - `Final Main Menu.unity`
    - `Final Circuit Monaco.unity`, `Final Circuit Baku.unity`, `Final Circuit Monza.unity`, `Final Circuit Spa.unity`, `Final Circuit Suzuka.unity`
- **Copyright-Safe Branding Rule:** Always keep car, tyre, and track branding original/non-infringing (`XPARROW`, `ORBITAL`).
- **Tools:**
  - Blender: `"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"` (`5.1.2`)
  - Unity: `"C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe"` (`6000.3.26f1`)
  - PowerShell Batch Build Quoting Rule (paths with spaces require nested quotes inside one string):
    ```powershell
    Start-Process -FilePath 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe' -ArgumentList "-batchmode -quit -projectPath `"D:\my Gits\game-dev-\game-dev-`" -executeMethod CircuitBuilder.BuildFinalGameAll -logFile `"<logPath>`"" -Wait -PassThru
    ```

---

## 2. Completed & Verified Work

### A. 5/5 Circuits: Gap Sealing & Solid `Col_Barriers` Containment
- **Track Edge & Pit-Lane Gap Sealing (`seal_all_track_gaps.py`):** Eliminated green runoff/terrain trenches between road asphalt, kerbs, pit lanes, and trackside barriers across Monaco, Baku, Monza, Spa, and Suzuka.
- **Solid Closed-Prism `Col_Barriers` (`rebuild_solid_barriers.py`):** Rebuilt `Col_Barriers` on all 5 circuits as closed `0.85 m`–`1.20 m` overlapping, `4.1 m`-tall (`-0.50 m .. +3.60 m`) outward-normal collision prisms, including Monaco tunnel/TecPro/pit-wall/buildings, Spa Endurance perimeter, and Suzuka's `d = 4720 m` crossover bridge seam.
- **Blender BVH Verification:** `missL=0, missR=0, backface=0, roadBlocked=0, pitBlocked=0` on all 5 circuits; fine `1.0 m` arc-length Suzuka check `0` misses.
- **Unity `VerifyBarrierContainment` ([CircuitBuilder.cs](file:///D:/my%20Gits/game-dev-/game-dev-/Assets/Race/Editor/CircuitBuilder.cs#L1708-L1760)):** Non-trigger `Physics.RaycastAll` every `4 m` along track left/right at `gy + 0.55 m` and `gy + 2.0 m` (`120 m` range):
  - `Monaco`: `[PASS] missL=0/828 missR=0/828`
  - `Baku`: `[PASS] missL=0/1483 missR=0/1483`
  - `Monza`: `[PASS] missL=0/1449 missR=0/1449`
  - `Spa`: `[PASS] missL=0/1749 missR=0/1749`
  - `Suzuka`: `[PASS] missL=0/1451 missR=0/1451`

### B. 2026 F1 Car, HD Liveries, Tyres, Pit Crew & High-Poly F1 22-Style Cockpit
- **Liveries, Tyres & Pit Stops:** 12 swappable HD team liveries (`Livery_00`..`Livery_11`), 5 Pirelli-inspired `XPARROW` tyre compounds, animated articulated pit crew & team-aligned garage stops (`PitLaneVerifyTest PASSED` — `11/11` grounded garage boxes on all 5 circuits + Baku full pit stop/wing repair/`115 km/h` pit exit with `barrierHits=0`).
- **HD Cockpit & Textures (`upgrade_cockpit_hd.py`, `generate_cockpit_textures.ps1`):**
  - Stripped old boxy mirrors from `F1_Body_LOD0/1/2`.
  - Built sculpted `F1_SteeringWheel_LOD0` (`2980 verts / 5604 tris`) with butterfly carbon faceplate, 12 labeled buttons, 4 knurled thumbwheels, 3 winged rotary dials (`STRAT`, `DIFF`, `BMIG`), 15 physical LEDs, and curled dark-charcoal micro-dot Nomex gloves.
  - Added sealed `F1_Cockpit_Interior_LOD0/1/2` carbon monocoque tub + column boot (`Y <= -0.630`, never blocking OLED), chamfered squircle chrome wing mirrors (`Mirror_Glass_LOD0/1/2`, toed `+12°` inward) on slim swept stays, nose pitot mast, and steered front-wheel wake deflectors (`F1_Cockpit_Deflector_FL/FR_LOD0/1/2`).
  - Wired `T_SteeringWheel_Body_Normal.png` + `Cockpit_Carbon.mat` in `CircuitBuilder.cs` (preserved by `RaceManager.ApplyLivery`, which only replaces `Livery_*` / `Mat_Team_*` materials), set `nearClipPlane = 0.03f`, and aligned `cockpitOffset = (0f, sw.y + 0.135f, sw.z - 0.405f)` (`cockpitPitch = 6.0f`).
- **512×320 OLED Display ([SteeringWheelDisplay.cs](file:///D:/my%20Gits/game-dev-/game-dev-/Assets/Race/Scripts/SteeringWheelDisplay.cs)):** Upgraded to `512×320` (`FilterMode.Bilinear`, zero-GC buffer update at 20 Hz) with 15 shift-light discs, `L<lap> / P<pos>`, `KPH`/`MPH`, large center gear numeral, `RPM`, `DRS` status pill, 4 tyre temp cards, and golden `ERS` battery bar.

### C. Simulation + GTA-Style Driving Physics & Camera ([F1Car.cs](file:///D:/my%20Gits/game-dev-/game-dev-/Assets/Race/Scripts/F1Car.cs), [ChaseCamera.cs](file:///D:/my%20Gits/game-dev-/game-dev-/Assets/Race/Scripts/ChaseCamera.cs))
- **Drivetrain & Aero:** `gearRatios = { 19.2f, 14.0f, 11.0f, 8.9f, 7.4f, 6.25f, 5.32f, 4.78f }`, `dragArea = 0.98f`, asphalt rolling resistance `4.5f` -> reaches **334 km/h** cleanly without 8th-gear bogging.
- **Steering & Suspension:** Progressive S-curve + speed-sensitive dual-rate keyboard smoothing, gentle high-speed counter-steer/yaw damping, plush asymmetric bump/rebound suspension (`restLength = 0.14f`, `springRate = 112000f`, bump `5600f` / rebound `8600f`, `antiRoll = 42000f`).
- **Anti-Tunneling & Crash Stability:** `rb.maxDepenetrationVelocity = 2.6f` + high-speed `SphereCast` lookahead (`closing > 25f`) split cleanly between head-on approach clamping (`<= 5.0 m/s` flush settle) and tangential glancing scrape (`!headOn`) + EditMode/PlayMode-consistent pinned-nose wall sensor & post-crash creep damping (`TripTime - lastImpactLogTime < 1.2f`).
- **Dynamic Camera:** 50% velocity-blended chase heading swing + longitudinal G distance lag + cockpit head surge/roll/apex yaw.

---

## 3. Latest Verified Benchmark & Test Metrics (`unity_overhaul_build.log`)

- **`UnityExitCode=0`** (`0` compile/script errors, `0` `Physics.ClosestPoint` errors)
- **`EngineAudio self-test PASSED`:** V10, V8, V12, V6 Turbo (`finite=True`, with 2-pole exhaust LPF + 15 Hz pit-limiter burble + reworked `WheelGun`/`CrashThud`/`ShiftClunk`)
- **`F1PhysicsTest`** ([F1PhysicsTest.cs](file:///D:/my%20Gits/game-dev-/game-dev-/Assets/Race/Editor/F1PhysicsTest.cs)):
  - Braking: `[PASS] Braking 334->0 km/h: 88.1 m in 2.55s (up.y 1.000)`
  - Head-on Crash: `[PASS] Wall hit 0° @ 264 km/h: z=9116.6 (noTunnel=True), maxRise=0.04m, bounce=0.03m/s, tilt=0.2°, wallNormRMS=0.000m/s, wingBreakRepairOK=True`
  - Glancing Crash: `[PASS] Wall hit 25° @ 264 km/h: z=9118.5 (noTunnel=True), maxRise=0.04m, bounce=0.00m/s, tilt=0.6°, wallNormRMS=0.046m/s, wingBreakRepairOK=True`
- **`FINAL BUILD SUMMARY: 5/5 circuits passed`:** Monaco (`3312 m`), Baku (`5932 m`), Monza (`5794 m`), Spa (`6994 m`), Suzuka (`5802 m`)
- **`BARRIER CONTAINMENT VERIFICATION: 5/5 PASSED`** (`missL=0, missR=0` on all 5 circuits)
- **`PitLaneVerifyTest PASSED`:** `11/11` grounded boxes on all 5 circuits + **Baku (`barrierHits=0`, `130 km/h`, gear 3)** & **Monaco (`barrierHits=0`, `128 km/h`, gear 3)** full pit stop + front-wing repair + pit exit

### E. F1 22 Cockpit Overhaul, Monaco Pit Exit, PlayMode Crash Physics & GitHub `<100 MiB` Fix (2026-11-26)
1. **F1 22-Style Cockpit, Wrists & OLED (`upgrade_cockpit_hd.py`, `F1Car.cs`, `ChaseCamera.cs`, `SteeringWheelDisplay.cs`, `CircuitBuilder.cs`):** Tucked wrist cuffs + clamped visual wheel lock (`±58°`) so steering never swings arms overhead; added matte carbon Halo shroud + wide V-gusset + slim teardrop pylon; added glossy team-painted `F1_Cockpit_Shoulders_LOD0/1/2` + coaming + twin aero fins, carbon front wishbones/pushrods, and enlarged wing mirrors (`0.082×0.036 m`); updated camera (`cockpitOffset = (0f, sw.y + 0.098f, sw.z - 0.305f)`, `cockpitPitch = 2.8f`, `55°–59°` FOV) and razor-sharp `512×320` OLED layout; verified via [unity_verify_cockpit.png](file:///C:/Users/USERAS/.gemini/antigravity/brain/9cf7120c-afb5-4b52-a65d-90ae4fdbb68f/scratch/unity_verify_cockpit.png) & [unity_verify_cockpit_steered.png](file:///C:/Users/USERAS/.gemini/antigravity/brain/9cf7120c-afb5-4b52-a65d-90ae4fdbb68f/scratch/unity_verify_cockpit_steered.png).
2. **Pit Lane Proportions & Monaco Pit Exit (`PitBox.cs`, `RaceManager.cs`, `CircuitBuilder.cs`, `monaco gp.blend`):** Disabled oversized `Pit_Stop_Equipment`, rescaled `PitProps_Cluster` (`0.52f`) on garage apron, tightened fast-lane crew/tyre-stack offsets, centered garage stops, opened Monaco's inner Sainte Devote pit-exit divider (`1,296` visual + `906` collider faces via `Road_Surface` frame filter) while maintaining `BarrierContainment Monaco: missL=0/828 missR=0/828`, flush-leveled exit merge vertices, and smoothed `PitPhase.Release`/`Exit` + `SetPitDriving` (`128–130 km/h` gear 3 handoff, `barrierHits=0`).
3. **PlayMode Crash Physics & Handling (`F1Car.cs`):** Restricted synthetic pre-contact wall lookahead clamping to `SimulationMode.Script` or `surfaceGap <= 0.05f && vLong > 38f`, upgraded `HandleBarrierCollision` with inelastic restitution, off-center `yawImpulse`, tangential scrub, and suspension bump compression, plus trail-braking front bite (`+10%`) & low-gear rear load progression (`0.96f`).
4. **GitHub `<100 MiB` Compliance (`spa gp.blend`, `Spa_GP.fbx`):** Replaced `565k`-poly `Grandstand_Crowd_LOD0` with `30k`-poly `Grandstand_Crowd_LOD1` copy in `spa gp.blend` (already deactivated at runtime by `CircuitBuilder.SetupMap()`), reducing `Spa_GP.fbx` from `100.17 MiB` to `78.25 MiB` (`82,046,044` bytes).

### F. Manual Pit Driving, 3-Phase Tyre Swap, PlayMode Crash Deflection & De-Screeched Audio (2026-11-26)
1. **Manual Pit Lane Driving (`F1Car.cs`, `RaceManager.cs`, `PitBox.cs`):** Removed the glitchy `pitRequested` main-straight hijack (`physicallyInPitEntry` only); added live `F1Car.pitLimiterActive` (`80 km/h` / `22.2 m/s`) so players drive manually into pit entry, into their glowing `PitBox` stop marker (docking smoothly over `~0.25 s` for the stationary `2.3 s` tyre swap), and manually out after the green release light (`testAutoPit` remains isolated to EditMode `PitLaneVerifyTest`), with full corridor track-limit suppression and live distance-to-box HUD prompt.
2. **Smooth 3-Phase Tyre Swap Choreography (`PitBox.cs`, `RaceManager.cs`):** Added exponential pose/position slerping in `CrewRig.AimBone`/`ApplyPose`, unified `Role.TyreOff` trajectory, animated gunner loosen/retract/clear gestures, and 3-phase axial hub slide + arc carry lift for `oldTyre` and `newTyre` synced cleanly to wheel visibility.
3. **PlayMode Crash Deflection (`F1Car.cs`):** Fixed `wallContactTimer` velocity clamping (`clampThresh = scriptSim ? 0.6f : 0f`) so cars never stick to barriers in PlayMode, added angle-aware inelastic rebound + wall-alignment yaw + lever spin, and continuous spark emission during wall slides.
4. **De-Screeched Audio & Real F1 Clip Drop-In (`EngineAudio.cs`, `GameSession.cs`):** Switched default engine to V6 Turbo Hybrid (`engineSound = 3`), shifted synthesis registers down into warm `70–650 Hz` fundamentals with sub-harmonic `0.25x`, dropped `3x/4x/6x` harmonics and `1,050 Hz` sine tyre squeal / pure-sine turbo whistles, lowered cabinet LPF to `480–1,060 Hz`, and added automatic single-clip loader for `Resources/F1Audio/f1_engine` (or `engine`).

### G. Playtest Video #2 Fixes: Monaco Pit Entry Colliders, Non-Stopping Wall Slides, Wide Cockpit FOV & Single Halo (2026-11-26)
1. **Monaco Pit Entry Blocker Cleanup (`monaco gp.blend`, `Monaco_GP.fbx`):** Removed `771` `Col_Barriers` and `203` `Pit_Entry_Exit_Barriers` interior blocker faces overlapping the `Pit_Lane` footprint (`y = -560..-460` pit entry + `-460..-120` overhangs) while maintaining `BarrierContainment Monaco: missL=0/828 missR=0/828`.
2. **Fixed Instant Wall-Stop Bug & PlayMode Wall Scraping (`F1Car.cs`, `CircuitBuilder.cs`):** Split `HandleBarrierCollision(c, isInitialImpact, ...)` so `OnCollisionStay` cancels only inward normal velocity with gentle `dt`-based scraping (`Mathf.Exp(-0.42f * dt)`) instead of multiplying tangential speed and yaw 100 times/sec; narrowed `pinnedHeadOnWall` and wheel normal projection in PlayMode (`absV < 4.0f`, `Dot < -0.75f` / `-0.60f`), and lowered `Barrier` physics friction to `0.04f`.
3. **Single Halo, Wide Panoramic `70°–76°` Cockpit FOV & Rear Jack Clearance (`upgrade_cockpit_hd.py`, `ChaseCamera.cs`, `CircuitBuilder.cs`, `PitBox.cs`):** Removed the duplicate carbon Halo tube/V-gusset/pylon so `F1_Body` provides the single clean Halo in TV-Pod and Cockpit views; widened Cockpit FOV to `70°–76°` (`cockpitOffset = (0f, sw.y + 0.125f, sw.z - 0.365f)`, `cockpitPitch = 3.6f`); shifted `Role.RearJack` work position clear of the rear wing (`(gx * 0.85f, 0, -4.05f)`).

### D. Playtest Video Fixes Completed (2026-11-26 — All 16 Identified Issues Resolved)
1. **Removed `Studio_Backdrop` from Car FBX (`F1_2026_Game.blend` & `CircuitBuilder.BuildCarPrefab()`):** Eliminated giant team-colored domes & travelling ice-blue floor planes across Main Menu and all 22 race cars (which had also caused flat light-grey polygons slicing across sloped corner crests).
2. **Monaco Visual Polish (`monaco gp.blend` + `EnhanceMaterials`):** Deleted 100 stray `Track_Crew` canopy/roof islands (`1618` faces), confirmed zero `Yellow_Line` highway strips on `Monaco_Road_Markings` (the yellow track dashes were Unity's runtime `BrakingMarkers` assist), textured `Tunnel_Wall`/`Tunnel_Ceiling`/`Monaco_White` with concrete albedo grain, and enabled realtime warm emission on `Tunnel_Light`.
3. **Realistic Athletic Pit Crew & Exact Limb Aiming (`build_pit_crew_assets.py` + `PitBox.cs`):** Reshaped Quaternius rigs (`head_scale=0.56`, `leg_stretch=1.28`, `fist_scale=0.62`), rebuilt `+Z->+Y` `Prop_WheelGun.fbx`, fixed the double-rotation `AimBone` bug (`neutralWorld * restLocalDir`) so gunners aim downward at wheel nuts instead of their own visors, offset `RearJack` to rear-quarter, and staggered all 20 idle crewmen cleanly in 2 rows along the garage apron.
4. **Pit Entry, Exit Barrier Clearance, 3/4 Pit Camera & Pit HUD (`CircuitBuilder.cs`, `RaceManager.cs`, `ChaseCamera.cs`):** Centered narrow pit chutes with multi-radius overlap + bilateral raycast midpoint clamping (`nudged 39` on Monaco), replaced `Collider.ClosestPoint` (invalid on non-convex `MeshCollider`) with `OverlapSphere` offset probes (`PitPosHitsBarrier`), smoothed `Approach` & `0.16 m` jack lift, added elevated 3/4 pit-stop camera during `PitServiceActive`, and collapsed post-swap pit HUD into a clean compound banner.
5. **Audio Overhaul (`EngineAudio.cs`):** Added per-channel 2-pole exhaust LPF, warmed harmonic order weights & combustion roughness, added 15 Hz pit-limiter exhaust burble, lowered hybrid whine, and upgraded `WheelGun`, `CrashThud`, and `ShiftClunk`.
6. **Hairpin Steering Lock, Chase Cam, AI & HUD Polish (`F1Car.cs`, `ChaseCamera.cs`, `RaceAI.cs`, `RaceManager.cs`, `CarHUD.cs`):** Raised low-speed lock to `30°` (`steerSpeed=135°/s`), tightened high-speed chase cam & FOV, calmed AI Lap-1 pace/impacts & yellow-flag cooldowns (`>15` impulse, `12s` trigger cooldown, `35s` radio cooldown), switched HUD scaling to `Mathf.Min(H/1080, W/1920)` with `24*s` safe margins, made `GapText` monotonic via circuit average speed, and added raycast occlusion + overlap culling (`max 6`) to `DrawNameTags`.

---

## 4. Critical Architectural Invariants (Remember Across Sessions)

1. **Blender-to-Unity Axis Mapping (`F1_2026_Car.fbx`):** Blender `(X_b, Y_b, Z_b)` maps to Unity `(-X_b, Z_b, -Y_b)` when nose points `-Y_b -> +Z_u`. Open cockpit camera `Blender (0.0, -0.18, 0.785)` equals `Unity (0f, 0.785f, +0.18f)`, staying cleanly ahead of the `Y = -0.10` airbox overhang.
2. **Unity `SimulationMode.Script` EditMode Behaviour:** `MonoBehaviour.OnCollisionEnter` and `OnCollisionStay` do **not** fire under `Physics.Simulate(dt)` in EditMode (`F1PhysicsTest` & `CircuitDriveTest`). Wall lookahead, pinned-nose clamping, and post-impact creep suppression MUST live in `F1Car.SimStep()`.
3. **`SurfaceGap` Pre-Contact Rule:** In `SimStep()`, never set `wallContactTimer` on a high-speed head-on deceleration frame before the nose reaches the wall collider (`lookaheadPreContact = true` skips `dvDrop`), otherwise `if (wallContactTimer > 0f)` zeroes velocity short of the collider and causes throttle slamming across the gap.
4. **`maxDepenetrationVelocity` Tunable Tradeoff:** Keeping `rb.maxDepenetrationVelocity = 2.6f` prevents high-speed barrier wedging (required for Monaco chicane autopilot lap completion), while `pinnedHeadOnWall` + `wallContactTimer` clamp prevents low-speed throttle oscillation.
5. **Mirror Loft Geometry:** Build mirror shells strictly monotonically front-to-back (`-Y` -> `+Y`) without tangent reversal, angle `+side * 12°` inward (`toe`), stop mirror stays below the glass floor (`Z <= 0.668`), and keep `Mirror_Glass` flat (`use_smooth=False`).

---

## 5. Pending Next Steps / Playtest Backlog

- [ ] **User Live Playtest Feedback (Post-Fix Pass):** Playtest updated Monaco (lights-out grid, Mirabeau/Fairmont Hairpin `30°` lock, Tunnel lighting, pit entry/service/exit, HUD edges/gaps/nameplates, and audio) plus Baku/Monza/Spa/Suzuka.
- [ ] **Git Commit & Push (User Action):** `git.exe` is not on system `PATH`; commit and push updated assets/scripts in `D:\my Gits\game-dev-\game-dev-` via GitHub Desktop or VS Code Source Control.
- [ ] **Optional Future Polish (If Requested After Playtest):** Wet-weather rain particles/spray tied to `Inter`/`Wet` compounds, race-start pit-grid engineers & blowers (`media_1795209529538_cc657427.jpg`), or custom team steering-wheel LED/badge variants.

### H. Claude Code inspection & fixes (2026-11-26)
- Inspection: 0 compile errors; F1PhysicsTest all PASS; auto-drive laps complete on all 5 Final circuits (no falls); menu + races run with 0 runtime errors.
- Performance: new `RaceOptimizer` (called in `RaceManager.Start`) tightens rival-car LODs (0.42/0.13/0.005), hides rival cockpit interiors/steering wheels/mirrors, and turns off shadows on rival small parts, pit crews and small scenery. Monza grid: 5.9M -> 3.3M tris, render 12.4 -> 5.6 ms. Draw calls still ~2,100 (circuit meshes; would need mesh combining).
- `PitBox`: no per-frame lambda lookups; idle crews > 160 m from the camera are hidden and not animated.
- Pit: stopping up to 15 m past the box still docks (crew pull back); radio warning when the box is missed.
- Audio: recorded engine loops in `Resources/F1Audio/<V10|V8|V12|V6Turbo>/engine_{idle,low,mid,high}.wav` (pitch-flattened from the user's comparison video, picked by Settings > Engine sound); `F1Audio/radio.mp3` replaces the synthesised radio beep. Synth remains the fallback.
- Tools: `Tools/XPARROW/Debug/Analyse Scene Weight`, `Log Performance`, `Check File Sizes for GitHub` (also run by Save All).
- Removed old duplicate scenes `Assets/Scenes/Circuit *.unity` + `Main Menu.unity` (Recycle Bin).

### I. IN PROGRESS - paused 2026-11-26 (user request: "fix everything")
Done this session (not yet re-tested in Unity after the folder move):
- Scripts organised: Race/Scripts/{Core,Car,AI,Pit,Audio,UI}, Race/Editor/{Builders,Tests,Tools} (moved with .meta, GUIDs kept).
- Removed (Recycle Bin): old Assets/Scenes/Circuit *.unity + Main Menu.unity, Generated/Sky.mat, Generated/Verge_Grass.mat, Resources/F1UI/Hero.png.
- Resources/F1UI images downscaled to 1024 px (60.7 MB -> 10.2 MB, importer already capped at 1024).
- New: Race/Editor/Tests/TrackAudit.cs (Tools/XPARROW/Debug/Audit Tracks (lines + pits)) - NOT RUN YET.
- Real crash SFX downloaded (user-approved, Epidemic Sound; check licence before publishing):
  Resources/F1Audio/Crash/{crash_heavy_1,crash_heavy_2,crash_heavy_3,crash_light,scrape_loop,gravel_loop}.mp3 - NOT WIRED IN YET.

TODO next (in order):
1. Refresh Unity, confirm 0 compile errors after the move; run Audit Tracks -> fix any circuit missing edge/lane lines
   (if missing, generate thin white edge-line strips at +/-halfWidth from RaceTrack at runtime - no file growth).
2. Verify pits on all 5 circuits (boxes, profile, entry/exit) + a pit stop in play mode.
3. Wire crash SFX in EngineAudio: crash_light (<8 m/s impact), crash_heavy_* (random, >8 m/s, volume by strength),
   scrape_loop while sliding along a barrier (F1Car wall contact), gravel_loop while OnGravel.
4. Runtime detail (no asset growth): braking boards 300/200/100 m before big corners, tyre smoke on slip,
   dust on gravel/grass, carbon debris burst on heavy impacts.
5. Physics: damage model (suspension damage -> pull + grip loss after heavy side impacts), better crash response;
   re-run F1PhysicsTest + Auto-drive laps on all 5 circuits.
6. Test races, Save All, update this file.

### J. Added tasks (2026-11-26, user) - do when the user says "continue"
7. Monaco tunnel: improve the entry (geometry/lighting/transition detail at the tunnel mouth).
8. Crowd noise: louder, positional crowd/cheer when passing grandstands and spectator areas (3D audio sources at stands).
9. Blender -> Unity fidelity: check every model (car, crew, props, 5 circuits) matches its Blender source (materials, textures, scale, missing parts).
10. Car model: if the 2026 car has defects, replace with a better non-copyrighted model (CC0/CC-BY, e.g. Sketchfab/Poly Haven/Poly Pizza) - ASK the user before each download (name, source, licence, size).
11. Main menu: visual upgrade + clearer, better organised layout.
12. Size & optimisation: remove unnecessary files/code, shrink the project and build as much as possible without changing the game
    (unused code paths, duplicate textures, oversized textures, .fbm extracted folders, debug-only tools out of builds, texture compression, mesh compression, audio compression).
13. Team liveries "exactly" like the real teams: NOTE - real sponsor logos/branding are trademarks; match real team colour schemes and layouts
    as closely as possible but keep logos/sponsor names original (project rule: copyright-safe XPARROW branding). Confirm with the user.
    Clarified by user (2026-11-26): only ONE car model is used, textured per team. If the current car model is fine, keep it and don't ask.
    Only if it has real problems: propose a free non-copyrighted replacement (ask once with name/source/licence/size before downloading).
    File locations: any 3D model / Blender source -> E:\My Game assets\f1 (next to the existing Blender models);
    game changes (imported FBX, textures, materials, scripts, scenes) -> the Unity project D:\my Gits\game-dev-\game-dev-.
14. Scenes look tilted in the editor (hard to edit by hand): the circuit FBX objects import with a -90 deg X axis rotation that the builder
    cancels with a +90 deg rotation on the circuit root. Fix at the source so every circuit root and child has a clean, upright transform
    (identity rotation): re-export/import with the correct axis conversion (Blender export axes or Unity importer "Bake Axis Conversion"),
    rebuild the Final scenes, and verify nothing moves (track data, colliders, pit boxes, grid, lines).
