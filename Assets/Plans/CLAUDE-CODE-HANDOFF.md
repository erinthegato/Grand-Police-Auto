# Lights & Sirens: Halden — Claude Code handoff

_Last updated 2026-11-18. Written for the next Claude Code session (or any coding agent)._

Read this first, then the specs in this folder:

1. **This file**: current state, how to drive the editor, architecture, what's left.
2. [`master-spec.md`](master-spec.md): the full game specification (§1–§21).
3. [`master-spec-4A-interiors.md`](master-spec-4A-interiors.md): multi-floor enterable interiors (§4A).

> ⚠️ **`Assets/GPA/Docs/SETUP.md` is stale.** It describes the old **3D** "City_Midtown" build
> (KINEMATION, WheelColliders, MegaKit), which has been removed. Its link to
> `Assets/Plans/UNITY-AI-HANDOFF.md` points to a file that does not exist. Don't follow it.
> Rewriting SETUP.md for the 2D game is an open task.

---

## 1. Project snapshot

| | |
|---|---|
| Project root | `C:\Users\joshy\Grand Police Auto` |
| Unity | **6000.6.9f1**, URP 17.6 with the **2D Renderer** (`Assets/Settings/Renderer2D.asset`, default on `PC_RPAsset`) |
| Game | 2D top-down police sim (GTA1-style + LSPDFR-style police systems) |
| Live scene | **`Assets/GPA/Scenes/City_Halden.unity`**, the only enabled scene in Build Settings (index 0) |
| Scene generator | `Assets/GPA/Editor/HaldenCityBuilder.cs` writes City_Halden (city grid, ~97 addressed buildings, props, road graph, signals, walk map, systems, player). **Anything added to the scene by hand must also be added here**, or a rebuild drops it. |
| Other scenes | `City_TopDown2D.unity` (older 2D test), `Assets/Scenes/SampleScene.unity` (disabled), `Assets/Script/TrafficSystem/Demo/Demo.unity` (third-party demo) |
| Persistent records | `%USERPROFILE%\AppData\LocalLow\Grand Police Auto\Grand Police Auto\Records\records.json` (2,500 citizens, ~1,349 vehicles, generated once with seed 1997) |
| Callout packs | `Assets/GPA/Resources/CalloutPacks/core_patrol.json` (7 callouts) + a mod folder in persistentDataPath |

### Git state (important)
- Repo has only **2 commits** (baseline + `.gitattributes`). **Almost all of the 2D game is uncommitted/untracked**: ~2,150 status entries outside `Assets/Cars`.
- `Assets/Cars/**` (old 3D police car models) shows as **deleted, uncommitted**. The **user** made that deletion. Don't restore or commit it without asking.
- Some tracked files (e.g. `Assets/GPA/Input/GPAControls.inputactions`, `Assets/GPA/Editor/GPA.Editor.asmdef`) carry earlier uncommitted edits from other sessions in addition to ours. Never `git add -A`. Stage only files you changed, and only when the user asks to commit.

---

## 2. Driving the Unity editor

### Use the Unity CLI, not the MCP server
- The **Unity MCP server cannot load in Freebuff sessions.** Freebuff's launcher hard-codes `strictMcpConfig: true` with only its own `freebuff` MCP server, so `~/.claude.json` / `.mcp.json` entries are ignored. This was verified in the launcher source; it's not a config mistake. The MCP server itself works (registered for the Claude **desktop** app and handshake-tested).
- Use the CLI instead. It exposes the same editor commands (scene, gameobject, component, script eval, capture, console, play mode, build, tests):

```bash
U="/c/Users/joshy/AppData/Local/Unity/bin/unity.exe"
export UNITY_PROJECT_PATH="C:\\Users\\joshy\\Grand Police Auto"
"$U" --no-banner status                       # expect: 7801  ready
"$U" --no-banner recompile                    # compile + report errors (exit 12 = Safe Mode / not reachable)
"$U" --no-banner command console get --level warn --tail 20 --result-only
"$U" --no-banner command scene list_open --result-only
"$U" --no-banner command script eval 'return UnityEngine.Time.frameCount;' --result-only
"$U" --no-banner command editor.playmode start   # / stop
"$U" --no-banner command capture game_view --width 1920 --height 1080 --output-path Previews/x.png
"$U" --no-banner command --query <term>       # search available editor commands
```

- **Run CLI commands with the sandbox disabled.** Sandboxed, the editor (port 7801) shows as `unreachable`.
- After `recompile`, or when leaving play mode, the editor reloads its domain. Wait with `status --until-ready --timeout 300` before the next command, or you'll get `EDITOR_NOT_RUNNING` / "Invalid response format". Those reloads can also leave two harmless `Pipeline ... HttpListenerResponse disposed` errors in the console.
- `console status` → `groundTruth.compilationFailed` / `consoleErrors` is the reliable compile check. The buffered `console get` can contain stale entries from earlier sessions.
- `Previews/` (project root) is the screenshot folder and is git-ignored.

### Play-testing input (big gotcha)
- The project uses the Input System with **"keyboards respect Game view focus"** and `BackgroundBehavior.ResetAndDisableNonBackgroundDevices`. Freebuff takes OS focus back after every tool call, so **keyboard events queued with `InputSystem.QueueStateEvent` are dropped or routed to the editor** and actions never fire. Focusing the Game view by script doesn't fix it.
- **What works:** a virtual gamepad plus a temporary background setting. Always restore it afterwards (it's an in-memory default, not a project asset):

```csharp
// setup
UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("MDTTestPad");
// tap a button: queue press AND release in the same eval
var pad = (UnityEngine.InputSystem.Gamepad)UnityEngine.InputSystem.InputSystem.GetDevice("MDTTestPad");
UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.South));
UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
// cleanup
UnityEngine.InputSystem.InputSystem.RemoveDevice(UnityEngine.InputSystem.InputSystem.GetDevice("MDTTestPad"));
UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices;
```

- Button taps work this way. **D-pad presses held across evals trigger key-repeat**: a separate press and release (≈0.3–0.6 s apart) repeats. For directional MDT navigation, call `MdtScreen.Navigate(Vector2Int)` via reflection instead. For a specific MDT button, set `FocusNav.Focused = "<widget id>"` via reflection, then tap A. Widget ids are listed in §4.
- Holding a d-pad for >0.5 s across two evals is fine when you *want* a hold (e.g. hold d-pad ↓ opens the MDT).
- **Clean up test data.** Records persist to disk. Remove any citations, reports or BOLOs a test creates (`db.Data.X.RemoveAll(...)`, unlink `CitationIds` / `IncidentIds`, `db.Save()`), and verify they're gone from `records.json`.

---

## 3. Architecture

### Assemblies (asmdef) and allowed dependencies
```
GPA.Core        (no GPA deps)       services, EventBus/GameEvents, game state, all contracts
GPA.Input       → Core              InputManager (state → action-map contexts), bindings, haptics
GPA.Vehicles    → Core, Input       legacy 3D-era vehicle code (WheelCollider etc.), likely unused by 2D (unverified)
GPA.Traffic     → Core              legacy EmergencyYieldHandler (unverified whether used)
GPA.TopDown2D   → Core, Input, Vehicles   2D world: Officer2D, Car2D, Ped2D, traffic, pedestrians, world/location DB, equipment
GPA.Records     → Core, TopDown2D   RecordsDatabase (JSON), PopulationGenerator, record models
GPA.Police      → Core, Input, TopDown2D, Records   TrafficStopManager, ArrestPipeline2D, InteractionMenu2D, OfficerCommands2D, HUDs
GPA.Callouts    → Core, TopDown2D   CalloutManager, scenarios, JSON packs, DispatchScanner (in-game radio)
GPA.HUD         → Core, Input
GPA.MDT         → Core, Input, Records, TopDown2D   the police computer (new this session)
GPA.Editor      → (most of the above, incl. GPA.MDT)   builders, setup, mobile build
```
**Independence rules from the spec that currently hold:** Police (traffic stops) does **not** reference Callouts. MDT does **not** reference Callouts or Police; it talks through Core contracts and events only. Keep it that way.

### Core patterns
- `Services.Register<T>() / TryGet<T>()`: service locator for cross-assembly interfaces.
- `GameEvents.Publish/Subscribe<T>` (struct events) for loose coupling.
- `IGameStateService`: a stack (base `OnFoot`/`InVehicle` + overlays such as `PoliceComputer`). `InputManager.DefaultContexts()` maps each state to its enabled action maps.
- `GameplayHotkeys.Blocked` (Core/GameState.cs): true while an overlay (MDT, pause, menu) owns input, or on the frame an overlay closed (`SuppressThisFrame()`). Several systems read **their own hard-coded keys** outside the managed input asset and must check this gate: CalloutManager (Y/N/O/End, d-pad ↑, View), OfficerCommands2D (T, d-pad ←), InteractionMenu2D. OfficerEquipment2D's number keys are already blocked by the officer control lock. **Any new raw-key reader must check `GameplayHotkeys.Blocked`.**

### Key contracts and events (Core)
| Contract / event | Implemented / published by | Used by |
|---|---|---|
| `IRecordsDirectory` | RecordsDatabase | world systems |
| `ILocationService` (Describe, NearestIntersection, CityName…) | CityLocations | dispatch, MDT |
| `IPlayerAvatar` (SetControlLocked…) | Officer2D | MDT, equipment |
| `IFreeRoamStopService` (`Active`) | TrafficStopManager | OfficerStatusService |
| `IDispatchRadio` (Transmit with priority) | DispatchScanner | callouts, stops, arrests, MDT (BOLO broadcasts, status) |
| `ICadCallSource` | **CalloutManager (wired this session)** | MDT dashboard/dispatch, OfficerStatusService |
| `IOfficerStatusService` + `OfficerStatusCodes` | **MDT/OfficerStatusService (new)** | MDT, CalloutManager (holds offers when Busy/TrafficStop) |
| `StopEndedEvent` | TrafficStopManager | MdtActivity (recent contacts, report-from-stop) |
| `DispatchLineEvent` | **DispatchScanner (wired this session)** | MdtActivity (dispatch history) |
| `OfficerStatusChangedEvent` | **OfficerStatusService (new)** | nothing yet (HUD could subscribe) |
| `DispatchTransmissionEvent` | DispatchScanner | intended hook for live-radio ducking (not built) |
| `CalloutPhaseChangedEvent` | CalloutManager | HUD |

### Records API (`GPA.Records.RecordsDatabase`, `Instance`)
`GetCitizen/GetVehicle/FindByPlate`, `SearchCitizens(q)`, `SearchVehicles(q)` (partial plate/VIN/ID), `VehiclesOf`, `WantedCitizens`, `CitationsOf`, `ArrestsOf`, `ActiveBolosFor`, `ReportsFor`, `AddCitation`, `AddArrest`, `AddBolo`, `SetBoloActive`, `SaveReport` (also keeps each record's `IncidentIds` in sync, including removals), `MarkDirty` (debounced save), `Save`.

---

## 4. The MDT (police computer), built this session

**Open:** `F1`, or **hold d-pad ↓ (0.5 s)** (`Interaction/OpenComputer`, works on foot or in the car). **Close:** F1 / Esc / gamepad View or B. While open: game state `PoliceComputer`, officer control-locked, world keeps running.

### Files (`Assets/GPA/Scripts/MDT/`)
| File | Role |
|---|---|
| `PoliceComputer.cs` | MonoBehaviour on the `Police Computer` scene object (also created by HaldenCityBuilder). Open/close, state push/pop, control lock, controller input (4-way nav with repeat, A, B, X backspace, Y space, LB/RB tabs, R-stick scroll). Owns `MdtActivity` and `OfficerStatusService` for the whole session and ticks status every frame. |
| `MdtScreen.cs` | Immediate-mode (OnGUI) screen, `partial`. Tabs, Persons/Vehicles/Warrants lookups, Reports editor, widgets (`Btn`, `TextBox`), drag-scroll, typing. |
| `MdtScreenCad.cs` | Dashboard (officer card, status buttons, shift stats, active call, recent radio, call types), BOLOs (list, composer, cancel/reinstate + broadcast), Citations, Dispatch (call history + radio log). |
| `FocusNav.cs` | Spatial controller navigation. Widgets register rects during Repaint; Move prefers aligned widgets (same row/column) before diagonal ones; scroll-view groups let focus reach off-screen rows; modal layer for the keyboard. |
| `MdtKeyboard.cs` | On-screen keyboard (no system/TouchScreenKeyboard is ever used). Shift symbols, caps, sentence auto-caps, Delete / Del word / Clear / New line / Done, live preview, suggestion chips. A physical keyboard also types into it. |
| `NarrativeBuilder.cs` | Report autofill: title + multi-paragraph narrative from type, place/time, attached people (identity, license, warrants, history, BOLOs), vehicles (registration, insurance, stolen, BOLOs), citations and arrests written that day, and the matching traffic stop outcome. Phrase chips (Observed / Contact / Actions / Disposition) with `{driver}/{vehicle}/{plate}` fill-ins. |
| `MdtActivity.cs` | Session memory: recent traffic stops (`StopEndedEvent`), radio history (`DispatchLineEvent`, cap 300), nearby peds/cars with record IDs. |
| `OfficerStatusService.cs` | Status = off duty → 10-7; stop → 10-38; call en route/on scene → 10-76/10-97; else officer's choice of 10-8 / 10-6. Only manual choices are radioed. |
| `MdtStyles.cs` | Shared textures/styles. |

**Tabs:** Dashboard · Persons · Vehicles · Warrants · BOLOs · Citations · Reports · Dispatch. On Persons/Vehicles an empty search shows **Recent contacts & nearby**. Desktop users can just start typing to search.

**Widget ids** (for tests via `FocusNav.Focused`): `tab:<i>`, `row:<tab>:<i>`, `field:pq|vq|wf|bf|cf`, `det:newrpt`, `det:bolo`, `det:veh:<i>`, `det:owner`, `dash:st:0|1|2` (10-8/10-6/10-7), `dash:accept|decline|clear|report`, `rpt:new|fromstop|autofill|undo|title|location|narrative|type-|type+|here|cat:<i>|phrase:<i>|save|discard|sugp:<i>|sugv:<i>|findp|findv`, `bolo:new|showall|issue|discard|toggle|report|reason:<i>|subject|reason`, `cit:person|veh|report`, `disp:report`, keyboard `osk:r<row>:<i>|shift|back|caps|space|newline|delword|clear|done|sug:<i>`.

### Input bindings added (`Assets/GPA/Input/GPAControls.inputactions`)
- `Interaction/OpenComputer`: `<Keyboard>/f1`, `<Gamepad>/dpad/down` with `Hold(duration=0.5)`. Tapping d-pad ↓ stays free for the unbuilt radio (`PoliceRadio/OpenRadio`).
- `PoliceComputer/Backspace` (`buttonWest`), `PoliceComputer/Space` (`buttonNorth`).

### Other files changed this session
`Core/GameState.cs` (GameplayHotkeys), `Core/CadContracts.cs` (IOfficerStatusService, OfficerStatusCodes, `CadCall.Time`), `Callouts/CalloutManager.cs` (ICadCallSource, history, hotkey gate, holds offers when busy), `Callouts/DispatchScanner.cs` (publishes DispatchLineEvent), `Police/OfficerCommands2D.cs` + `Police/InteractionMenu2D.cs` (hotkey gate), `Records/RecordsDatabase.cs` (queries, report linking, SetBoloActive), `Editor/HaldenCityBuilder.cs` + `Editor/GPA.Editor.asmdef` (Police Computer object), `City_Halden.unity` (Police Computer object), Build Settings (City_Halden only).

---

## 4B. Minimap (added 2026-11-18)

- `GPA.Minimap` assembly (→ Core, Records, TopDown2D). `Minimap2D` on the `Minimap` scene object (also created by HaldenCityBuilder). Top-right IMGUI panel; hidden while `GameplayHotkeys.Blocked`.
- City pre-rendered once from `CityLocationDatabase` into a texture; each frame drawn with GL into a 512px RenderTexture (route, markers, player arrow).
- Markers come from `GPA.Core.MapMarkers` (`IMapMarkerProvider`, `MapMarker`, `MapMarkerKind` in `Core/MapContracts.cs`). Providers: CalloutManager (offer blinks; accepted call has `Route = true`), TrafficStopManager (stop / pursuit / foot chase). BOLO cars are found by the minimap itself (active BOLO `VehicleId` vs `Car2D.VehicleRecordId`, refreshed every 1 s).
- GPS: `RoadNetwork` (intersections joined along each street, Dijkstra). One-way ignored.
- Status badge from `OfficerStatusChangedEvent` / `IOfficerStatusService`. Place line from `ILocationService.Describe`.
- **M** toggles north-up / rotating (PlayerPrefs `GPA.Minimap.Rotating`, default north-up). Not in the rebindable input asset; no gamepad binding.
- Testing tip: an unfocused editor stalls play mode at frame 2. Set `Application.runInBackground = true` (reset afterwards) and use `ScreenCapture.CaptureScreenshot(path, 3)` for readable HUD shots.

## 5. Spec status (from a grep-level survey plus the MDT work; verify before relying on it)

| Spec | Status |
|---|---|
| §3 2D rendering/camera | Mostly done (2D renderer, sorting layers, sprite atlas, Camera2DFollow) |
| §4 City / location DB | Done for Halden (generated fixed scene, addresses, intersections). No stop signs found. One-way streets exist in the builder. |
| §4A Interiors | **Done (core).** Every building is enterable (`TopDown2D/Interiors/`): procedural floors, rooms, doors (locked/forceable), stairs/lift, room concealment, residents from records, floor in dispatch/MDT location text. Precinct 3 floors, apartments up to 6. Indoor callouts done (`IndoorCallScenario`). |
| §5 Pedestrians | Partial (wander, personalities, crossings via WalkMap2D) |
| §6 Traffic | Partial (road graph, signals, TrafficDriver2D, density) |
| §7 Records | Done (JSON, persistent, corrupt-file recovery) |
| §8 MDT/CAD | **Done this session** except a dedicated arrest-records tab. Call history and the radio log aren't persisted across sessions. |
| §9–§11 Interactions, traffic stops, police vehicle | Partial but substantial (TrafficStopManager 538 lines, ArrestPipeline2D, lightbar, siren) |
| §12 Weapons | Partial (OfficerEquipment2D: pistol/rifle/shotgun/taser/baton/spray/cuffs, raycast fire, reload, wheel) |
| §13 Live radio stream + ducking | **Done.** `Scripts/Radio/` (NLayer MP3 streaming, duck/pause modes, RadioMenu incl. paste-link, settings saved). |
| §14 Callout framework | **Done.** `ICallout`, `ICalloutFactory`, `ICalloutProvider`, `[CalloutType]` auto-discovery, `CalloutDefinitionAsset` / `CalloutPackManifest` ScriptableObjects, `CalloutValidator`, JSON packs still supported. 18 callouts, 4 scenario types. |
| §16 HUD / minimap | HUD partial (IMGUI). **Minimap done** (`Assets/GPA/Scripts/Minimap/`, see §4B). No full-screen map or player waypoints yet. |
| §17 Input | Done (8 action maps, rebinding persistence, device-family glyphs) |
| §18 Save / settings | **Done.** `Scripts/Game/SaveGame.cs` (position, cruiser, time, duty, tally; autosave 2 min) + title/pause menu `GameMenu2D` (skipped in editor unless Tools ▸ GPA ▸ Show title screen in editor). |

## 6. Known issues and gotchas
- **SETUP.md is stale** (see top). Rewrite it for the 2D game.
- **Mobile:** the build is controller-only by design; Android/iOS build modules are **not installed** on this PC. MDT touch taps and drag-scroll were only tested with simulated mouse events, never on a device. There's no touch way to open the MDT.
- **MDT text editing is append-only** (no caret movement). Delete / Del word / Clear cover corrections.
- **MDT tab labels clip** on screens much narrower than 16:9 (8 tabs share the width).
- **Callout location text can be redundant** ("Grand Blvd near Bridge St, near Grand Blvd & Bridge St") because `CalloutManager` appends the nearest intersection to `ILocationService.Describe`, which may already name it.
- The minimap's GPS line ignores one-way streets (a guide, not a legal route).
- **Legacy 3D assemblies** (`GPA.Vehicles`, `GPA.Traffic`, `Assets/Script/TrafficSystem` third-party) may be unused by the 2D scene. Check scene GUID references before removing anything.
- **`CalloutManager`'s own hotkeys** (Y/N/O/End, d-pad ↑, View) are hard-wired, not in the rebindable input asset.
- **Warning:** `ArrestPipeline2D.cs:72` uses obsolete `FindFirstObjectByType`.

## 7. Suggested next steps (updated 2026-11-18 evening)
Done since the list below was written: radio (§13), interiors (§4A), indoor calls, callout framework (§14),
title/pause menu + save game (§18), roofs, ring road + bridge roadblocks, phone UI scaling (`Core/UiScale`),
iOS build script (`Editor/IOSBuild.cs`) and GitHub Actions workflow (`.github/workflows/build-iphone.yml`, see BUILD-IPHONE.md).
Still open: commit the work to git; first iPhone build + device test; SETUP.md rewrite; §15 dispatch procedures
(backup requests, officer emergencies, configurable radio codes); full-screen map/waypoints; MDT arrest-records tab;
on-screen touch controls (optional).

### Original list
1. Rewrite `Assets/GPA/Docs/SETUP.md` for the 2D game and commit the 2D work (ask the user; stage only intended files).
2. ~~Minimap~~ done 2026-11-18. Possible follow-ups: full-screen map, player-set waypoints, a gamepad binding for the north-up/rotate toggle.
3. **Live police radio + ducking** (§13A/C/D/E): a streaming subsystem separate from DispatchScanner, a central Radio Audio Manager listening to `DispatchTransmissionEvent`, Duck/Pause/Stop-Reconnect modes, persisted settings. The MDT could host the radio settings tab.
4. **Callout framework layering** (§14): `ICallout`, `ICalloutFactory`, `ICalloutProvider`, ScriptableObject definitions, while keeping `ICadCallSource` as the MDT boundary.
5. **Interiors** (§4A): biggest remaining feature; needs design (floors, doors, visibility groups) before code.
6. Small MDT follow-ups: arrest-records tab, persist call history/radio log if wanted, a "use computer" prompt when seated in the cruiser.

## 8. Verification checklist for MDT changes
1. `recompile` → no errors; `console status` → `compilationFailed: false`.
2. Play mode; confirm frames advance and `[Records] Loaded …` appears.
3. Virtual pad: hold d-pad ↓ → `PoliceComputer.Instance.IsOpen == true`, state `PoliceComputer`.
4. Exercise the changed widget via `Focused = id` + tap A; assert records/CAD state via `script eval`.
5. `capture game_view` and **look at the screenshot**: layout bugs (overflowing tabs, clipped labels, missing glyphs like ⌫/↵ in the default font) only show up visually.
6. Remove test data, remove the pad, restore `backgroundBehavior`, stop play mode, and check the console for warnings.
