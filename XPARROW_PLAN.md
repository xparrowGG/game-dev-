---
name: xparrow-asset-integration-plan
description: "Plan + findings for replacing procedural Monaco with the user's real F1 assets (E:\\My Game assets\\f1) in the XPARROW F1 GP Unity game"
metadata:
  node_type: memory
  type: project
  originSessionId: 6268866c-74a5-4ff5-a328-3715493f1997
  modified: 2026-11-19T21:48:30.918Z
---

Request (2026-11-20): use E:\My Game assets\f1 (5 circuits, 2026 F1 car w/ 12 liveries, tyres/pit props), remove the procedural Monaco map, assemble a full game: better F1 physics, settings, opening menu with extra features. User asked me to remember the plan in case the session stops.

Unity project: D:/my Gits/game-dev-/game-dev-/Assets (Unity 6000.3 URP, driven via Unity MCP; RunCommand blocked -> write [MenuItem] editor scripts, Assets/Refresh, run via ManageMenuItem; stop Play mode before building).

Plan steps:
1. Inspect assets (done).
2. Blender CLI (`C:\Program Files\Blender Foundation\Blender 5.1\blender.exe --background X.blend --python s.py -- args`): export car FBX (delete cameras/lights/Studio_Backdrop/F1_Internals; keep F1_Car hierarchy + LODs + Collider_Body). Copy textures from `formulla 1 & tyres\textures\{teams,shared,tyres}` to Assets/F1Assets/Car. Build 12 URP materials (T_XX_Team_BaseColor/Metallic/Roughness + shared T_F1_2026_AO/Normal/Emission). Whole car uses ONE atlas material Mat_Team_XX (00 Concept, 01 Ferrari, 02 RedBull, 03 McLaren, 04 Mercedes, 05 AstonMartin, 06 Alpine, 07 Williams, 08 RacingBulls, 09 Haas, 10 Audi, 11 Cadillac). LOD0 ~130k tris -> set LODGroup from _LOD0/1/2 suffix.
3. Per-map Blender extraction -> JSON track data (Blender coords), then copy FBX into Assets/F1Assets/Tracks/<name>. In Unity calibrate Blender->Unity axis mapping by matching Col_Track_Asphalt bounds.
4. Scene builder: Col_* -> MeshCollider, renderer off; hide Checkpoint/Trigger/DRS/Reset/Track_Limit/AI_/Marker objects; RaceTrack from centreline; grid computed from centreline (Grid_Slot empties are ALL at origin - useless); pit path from AI_Pit_Lane; DRS zones; 22 cars.
5. Add Cadillac team (Perez, Bottas) -> 11 teams / 22 cars. Adapt F1Car/RaceAI wheels to Steer_XX/Spin_XX.
6. Menu: track select (render/00_Overview.png), garage/livery viewer, calendar over 5 tracks, time trial, credits with "(c) OpenStreetMap contributors", settings, controls. Physics: grass/kerb/gravel grip, DRS.
7. Remove procedural Monaco GP scene/MonacoBuilder from build; test.

Findings (Monza, likely same for Baku/Spa/Suzuka): AI_Racing_Line, AI_Pit_Lane, Track_Limit_Left/Right are 0.5 m-wide ribbon meshes, verts in L/R pairs (use pair midpoints), ~2 m steps, world-space, identity transform. Centreline = average of Track_Limit_Left & Right midpoints (~12 m apart). Trigger_Start_Finish / Checkpoint_* / DRS_* are 8-vert box meshes (use centre). Monaco: no GRP root, markers useless, AI lines are curves; centreline in source_data/osm/lap.npz (lap.json only has street names).

STATUS 2026-11-20: DONE. Assets/Race/Editor/CircuitBuilder.cs (Tools > XPARROW menu) builds car prefab (Assets/F1Assets/Car/F1_2026.prefab, liveries in Resources/F1Liveries) and scenes "Circuit Monaco/Baku/Monza/Spa/Suzuka". Gotchas: car FBX must be exported with bake_space_transform=False; circuit FBX objects import with -90 X rotation -> builder cancels it at the root. Tools > XPARROW > Play Open Circuit (test) skips the menu redirect. Old Monaco scene + MonacoBuilder.cs moved to Recycle Bin. Not yet done: tyre/pit-props pack unused; a human playtest of driving feel on the new tracks.
