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
