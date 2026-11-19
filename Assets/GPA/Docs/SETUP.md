# Grand Police Auto — Setup

Unity **6000.6.9f1**, URP 17.6. Project root: `C:\Users\joshy\Grand Police Auto`.

> Working with an AI agent on the player/weapons/KINEMATION code? Start with
> [`Assets/Plans/UNITY-AI-HANDOFF.md`](../Plans/UNITY-AI-HANDOFF.md) — architecture, regeneration steps,
> headless verification commands and known issues.

## Open & play
1. Open the project in Unity Hub (6000.6.9f1).
2. Open `Assets/GPA/Scenes/City_Midtown.unity` and press Play: patrol Midtown on foot
   (WASD, V toggles first/third person, C crouches, E interacts) or press E at the patrol
   cruiser to drive it.
3. `Assets/GPA/Scenes/Sandbox_Player.unity` is still the controller-only sandbox.

## Audio overhaul — continuation status
- Midtown remains the playable foundation and is to be expanded, not replaced or deleted.
- Emergency lighting uses Off → Stage 1 steady → Stage 2 → Stage 3 → Off. Siren activation
  now requires Stage 2 or 3; dropping to Stage 1 or Off stops the siren. Existing vehicle
  bindings are `1` for lights and `Q` for siren (LB/RB on gamepad).
- Taser cartridge fire and discharge arcs now use separate audio sources. Arc playback is
  stopped on discharge end, reload or holster; clip-based discharge arcs loop for the cycle.
- Weapon, taser and siren scripts now have serialized mixer-group references for runtime-created
  sources. These references are not yet wired because MainMixer has not been created.
- All edited runtime scripts compile. Runtime/listening verification is still pending.
- Four NYPD siren WAV assets already exist (about 10.03 s each, 44.1 kHz); no regeneration
  is currently needed. Their loop quality and prefab assignments remain to be verified.
- Native AudioRandomContainer and AudioMixerController authoring commands failed in the current
  editor automation. Container creation, mixer creation/routing and builder integration remain open.
- Fleet audit found missing door textures and livery materials incorrectly assigned to interior,
  glass or light renderers on several prefabs; cars 10 and 13 also lack body texture assignments.
- Midtown currently has no scene traffic-driver components, so emergency yielding still needs
  an actual traffic integration rather than just attaching a speed modifier.

## Project state: reverted to Midtown
The playable build is **`Assets/GPA/Scenes/City_Midtown.unity`** — the hand-built MegaKit city with the
park plaza, Novexel waypoints and real-scale (1:1 metre) geometry. It is the only enabled scene in
Build Settings. The generated `City_Downtown` scene, its minimap snapshot, volume profile,
district profile and the `PatrolCityBuilder` pipeline have been deleted from the project.

- The officer is back to a **1.8 m** capsule with eyes at 1.68, walk 2.2 / sprint 5.8 / crouch 1.3 m/s,
  3.2 m third-person boom and a 4 m interaction search radius — the pre-generator tuning, which is
  what Midtown's real-scale buildings and doors expect.
- **How it was recovered:** M2 deleted `City_Midtown.unity` and its layout scripts, the repository has
  no commits, and the Recycle Bin was empty. The only surviving copy was Unity's crash-recovery scene
  `Assets/_Recovery/0.unity`, restored as `City_Midtown.unity`. It is intact — 1176 renderers, zero
  missing scripts, Player/HUD/GameSystems/World/WaypointManager all present.
- **What is gone for good:** the Midtown layout builders (`CityLayout`, `BuildingAssembler`,
  `KitCatalog`, `ProceduralProps`). The city can be edited by hand but not regenerated.
- The M3 vehicle systems remain in the project but Midtown has no cruiser placed in it, so they are
  dormant until a car is added to this scene.

## World scale (City Generator scene, now disabled)
The City Generator pack is miniaturised: its characters stand **0.722 units** tall and its cars are 3.1
units long, so the officer is built to the pedestrians' scale rather than to real-world metres.

| Player | Value |
|---|---|
| CharacterController | height 0.72, radius 0.2 (same as a pedestrian's capsule), centre y 0.36, step 0.16, skin 0.02 |
| Eye height | 0.67 (0.93 × height), 0.5 under the 0.722-tall character model's head top |
| Crouch | 0.46 |
| Speeds | walk 0.88, sprint 2.32, crouch 0.52 units/s (roughly 0.4× the old 1.8-unit values) |
| Reach | interaction search radius 1.6, cruiser interaction range 1.4 |
| Driver seat | 0.4 above the car floor, so the seated eye (1.07) stays under the 1.3-tall roof |

`PlayerMotor.WorldScale` records the 0.4 factor. Keep new distances, speeds and reach values in this
scale — a 1.8-unit capsule puts the camera two head-heights above every pedestrian in the city.

## Milestone preview (run this at the end of every milestone)

**Play it yourself:** `GPA/Preview/Play Midtown` opens `City_Midtown.unity` and enters Play mode.
Walk with the left stick, drive by pressing **E** at the cruiser, and the city keeps simulating
around you.

**Capture it for the record** (three PNGs in `Previews/`, then Play mode exits):

```
# With the project open in the Editor (any Unity 6 with the Unity CLI installed):
unity command editor.playmode start
unity command capture game_view --width 1920 --height 1080 --output-path Previews/preview_01.png
unity command capture game_view --width 1920 --height 1080 --output-path Previews/preview_02.png
unity command editor.playmode stop
```

The in-Editor equivalent is `GPA/Preview/Capture Preview Screenshots`, which does the same from a
menu item and logs the camera state, renderer counts and draw materials to the Console.

> **Capture gotcha:** `ScreenCapture` reads the Game view's own render target. In a scripted Editor
> session started with `-executeMethod`, with no Game view tab showing, that buffer is uninitialised
> and the PNG comes out as flat cyan garbage even though the game renders fine. Focus a Game view
> first (`Game > Game`), or use `unity command capture game_view` above, which always renders the
> real thing.

`Previews/` is ignored by the workspace, so screenshots never get committed.

## City pipeline (removed)
The generated Downtown city is gone: `City_Downtown.unity`, its minimap snapshot, its volume and
district profiles and the `Assets/Editor/PatrolCityBuilder.cs` builder that produced them were
deleted. Midtown is hand-built and is the only city scene.

The embedded **City Generator** package (`Packages/com.santiandrade.citygenerator`, MIT) is still in
the project, and the art it shipped with is still used: Midtown's street and building meshes come
from `Assets/City/Downtown City MegaKit[Standard]`. That folder is art, not the deleted scene — do
not remove it.

### Layout facts (measured from the generator, not guessed)
- Blocks sit on a **56 m pitch** (46 m block + 10 m street) in a **6 × 6** grid, so the city is
  a 336 m square centred on the origin and ends in walkable sidewalk.
- Traffic and pedestrians are the generator's own agents (`TrafficManager`, `PedestrianManager`,
  `CarAgent`, `PedestrianAgent`); they follow its traffic lights and pedestrian graph.
- The player's cruiser is the generator's `PoliceCar` model with `CarAgent` removed and GPA's
  `PoliceCruiser` (Rigidbody + arcade forces) added. It carries a `TrafficObstacle`, so ambient
  generator cars brake for it; the player's "Player" layer is also added to every `CarAgent`'s
  `pedestrianMask`, so they brake for the officer on foot too.
- The officer body is the generator's `Character-Male-D`, the closest thing the pack ships to a
  "police pedestrian": the demo characters are one-atlas civilians with no police variant, so the
  model is used as-is (a navy uniform tint would have recoloured the face and hair too).
- The generator carries no street metadata, so `DistrictProfile`/`StreetSegment` stay
  district-level until an address service is built on the generated grid.

## Vehicles (Milestone 3, first slice)
The cruiser is driven by real WheelCollider physics shared with the future AI drivers, not the old
arcade force model.

| Component | Where | What it does |
|---|---|---|
| `VehicleController` | `GPA.Vehicles` | Rigidbody + 4 WheelColliders, torque curve by gear, brake/handbrake, speed-sensitive steer lock, downforce. Fits its own colliders to any child named `wheel*` at Awake, so new car models need no per-prefab physics work. |
| `IVehicleControlInput` | `GPA.Core` | Throttle/brake/steer/handbrake only. `PlayerVehicleInput` reads the Vehicle action map; M4's traffic driver will implement the same interface. |
| `PoliceCruiser` | `GPA.Vehicles` | Gameplay facade: identity, seats, `IInteractable` enter/exit, equipment buttons, publishes `EmergencySignalChangedEvent`. |
| `EmergencyLightbar` | `GPA.Vehicles` | Off / cruise / code 3, driven by a `LightbarPattern` asset, with a small emissive lens created at each `Beacon*` lamp (the model has no lightbar geometry). |
| `SirenController` | `GPA.Vehicles` | Wail / yelp / priority plus horn, **synthesised at Awake** — no audio files ship. Assign an `AudioClip` to a tone to replace it. |
| `VehicleCameraRig` | `GPA.Vehicles` | Chase camera, cockpit view at the `DriverSeat` with free look, hold-to-look-behind. `ToggleCamera` switches chase/cockpit. |
| `EmergencySignalRegistry` | `GPA.Core` | Live list of signalling vehicles for M4 traffic yielding; cleared on every load. |

Controls while seated: **Throttle/Brake** triggers, **Steer** stick, **Handbrake**, **EmergencyLights**
cycles off → code 3 → cruise, **Siren** toggles, **SirenTone** cycles the tone, **Horn**, **ToggleCamera**,
**LookBehind** (hold), **ExitVehicle**.

Measured on the generated cruiser (1500 kg, 3.1 units long): 0→33 km/h in 2 s, 51 km/h at 4 s, top
speed capped at 90 km/h, ~34° of heading change in 3 s at 0.7 steer, wail clip 4.00 s and playing.

**Not built yet** (needs art this pack does not have): separate door meshes to animate, head/brake/
reverse light meshes, a number-plate mesh with TMP text, and `VehicleIdentity`/`VehicleRecord`
linkage. The civilian side of M3 — `PoliceToCivilianConverter` and `ProceduralVehicleGenerator` — is
also outstanding: the generator's car is one body mesh with a single shared texture atlas, so
repainting it would recolour the wheels too.

## Mobile builds (Android / iOS)
The build is controller-only: no touch UI. An MFi controller on iOS (or any standard HID gamepad on
Android) arrives as an Input System `Gamepad`, and every gameplay action already has a gamepad
binding, so the on-foot and driving controls work unchanged. Glyphs fall back to the generic set
for MFi pads.

| Step | Menu item | What it does |
|---|---|---|
| 1 | `GPA/Mobile/Apply Mobile Player Settings` | Landscape-only (portrait autorotate off), IL2CPP + ARM64, ASTC, Vulkan→GLES3, Android min API 26, iOS 13+ full-screen, bundle id `com.grandpoliceauto.downtownpatrol`, and `City_Midtown` added to the build list. |
| 2 | `GPA/Mobile/Build Android APK` / `Build Android AAB (Play Store)` / `Build iOS Xcode Project` | Applies the settings, pins the URP **Mobile** quality level, builds `City_Midtown` only, and writes to `Builds/Android/` or `Builds/iOS/`. |
| — | `GPA/Mobile/Verify Mobile Configuration` | Logs the effective settings and whether each platform module is installed. |

Headless:

```
Unity.exe -batchmode -quit -projectPath . -executeMethod GPA.EditorTools.MobileBuild.BuildAndroidApk
Unity.exe -batchmode -quit -projectPath . -executeMethod GPA.EditorTools.MobileBuild.BuildAndroidBundle
Unity.exe -batchmode -quit -projectPath . -executeMethod GPA.EditorTools.MobileBuild.BuildIosProject
```

**Prerequisites:** the platform module must be installed in Unity Hub (**Installs → 6000.6.9f1 →
Add modules**): *Android Build Support* (with *OpenJDK*, *Android SDK & NDK Tools*) or *iOS Build
Support*. Without it the build methods fail with an explicit `BuildFailedException` naming the
missing module. Android release builds currently use Unity's debug keystore — add a keystore
before publishing. The iOS export is an Xcode project; producing and signing the `.ipa` needs
Xcode on macOS.

The mobile build uses the project's URP **Mobile** level (`Assets/Settings/Mobile_RPAsset`: 0.8
render scale, MSAA off, one shadow cascade, 50 m shadow distance). Desktop keeps the **PC** level
(`PC_RPAsset`); the per-platform defaults in `QualitySettings` already select each correctly.

## Known gaps at this stage
- `PoliceCruiser` is an arcade force model (no `WheelCollider`s), has a single chase camera and
  flashing beacons but no siren audio.
- GPA's `TrafficDriver`/Novexel waypoint traffic (plan M4) is not wired to the generator's
  traffic graph yet; generator cars drive their own network.
- No NavMesh bake (plan 2.8) and no lightmap/occlusion bake (plan 2.9).
- The generator's crowd avoidance looks for its own `PlayerController`, so generated pedestrians do
  not step around the GPA officer yet (they do stop for the cruiser, which is a `TrafficObstacle`).
- **Android and iOS build support are not installed on this machine** (only Windows), so no APK,
  AAB or Xcode project has been produced yet. Mobile performance on device is unmeasured: the
  city ships 45 physics vehicles and 90 pedestrians, which is the first thing to profile on a
  phone. There is no pause/settings menu and no touch fallback, so a device without a gamepad is
  unplayable by design.
- Milestones 3+ (traffic stop, pursuit, MDT, dispatch, firearms, callouts) are unstarted.
