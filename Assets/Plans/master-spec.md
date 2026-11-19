# Unity 2D Open-World Police Simulator — Master Development Specification

## 1. Project Overview

Build a complete, standalone 2D open-world police simulator inspired by the original Grand Theft Auto (1997), with deeper police simulation mechanics inspired by LSPDFR.

This is an independent game, NOT a GTA modification.

**Core visual direction:**
- 2D top-down perspective.
- Classic GTA1-inspired presentation.
- Modern American city setting.
- Orthographic camera looking down at the world.
- 2D sprites, sprite animations, and tile-based environments.
- Detailed roads, buildings, vehicles, pedestrians, and environmental objects.
- A modernized visual presentation while retaining the classic top-down gameplay style.

The game should feature:
- A large, explorable modern city.
- On-foot and vehicle gameplay.
- Civilian traffic and pedestrian AI.
- Realistic police traffic stops.
- Interactive civilians and vehicle occupants.
- Police dispatch and callouts.
- Live police radio streaming.
- An in-game police radio that interacts with the live feed.
- Functional police CAD/MDT.
- Persistent citizen and vehicle records.
- Police vehicle equipment.
- Firearms and basic combat.
- An extensible callout framework.
- Keyboard/mouse and controller support.

The game must be playable as a standalone Unity project.

## 2. Existing Project Inspection

Before implementing anything:
1. Inspect the entire existing Unity project.
2. Identify existing scripts, scenes, sprites, tilemaps, animations, audio, packages, and systems.
3. Reuse existing functional assets wherever possible.
4. Preserve existing functionality.
5. Avoid recreating working systems.
6. Keep third-party assets separate from custom scripts.
7. Organize all systems into modular, maintainable components.

Use 2D assets rather than creating 3D character and vehicle models.

## 3. 2D Visual Style and Camera

Create a polished top-down game inspired by GTA1.

### Camera
- Orthographic top-down camera.
- Camera follows the player.
- Smooth camera movement.
- Configurable camera zoom.
- Optional camera rotation.
- Support on-foot and vehicle camera behavior.
- Keep the player and nearby action visible.

### Visual presentation
Use detailed 2D sprites for:
- Civilian pedestrians.
- Police officers.
- Police vehicles.
- Civilian vehicles.
- Buildings.
- Roads.
- Sidewalks.
- Traffic lights.
- Street furniture.
- Weapons and equipment.
- Environmental objects.

Use sprite sheets and animation controllers for character and vehicle animations.

Maintain consistent sprite scale, lighting, perspective, and visual style.

Avoid mixing incompatible art styles.

### 2D rendering
Use Unity's 2D Renderer and URP where appropriate.

Implement:
- Sorting layers.
- Sprite atlases.
- Efficient sprite batching.
- Tilemaps.
- 2D colliders.
- 2D physics.
- Occlusion or visibility techniques appropriate for 2D.
- Efficient camera culling.

Buildings should support visual occlusion or fade-out behavior when they obstruct the player's view.

## 4. City Environment

Create a fixed, prebuilt modern American city with dense urban areas.

Use suitable free or paid 2D city assets where available. Generate original 2D assets only when necessary.

Include:
- Downtown.
- Residential neighborhoods.
- Commercial districts.
- Industrial areas.
- Shopping streets.
- Parking lots.
- Alleys.
- Highways.
- Intersections.
- Police stations.
- Emergency service buildings.

Build the map as a persistent Unity scene using Tilemaps and reusable prefabs.

Do not randomly regenerate the city every time the game starts.

### Roads and navigation
Create a road network with:
- Main roads.
- Side streets.
- One-way streets.
- Intersections.
- Traffic lanes.
- Sidewalks.
- Pedestrian crossings.
- Traffic lights.
- Stop signs.
- Parking areas.

Maintain a city location database containing:
- Street names.
- Addresses.
- District names.
- Intersection names.
- Buildings.
- Police stations.
- Incident spawn locations.

Callouts and dispatch should use these location records.

## 5. Pedestrian Sprites and Civilian AI

Use realistic-looking 2D top-down pedestrian sprites.

Create or source sprite sheets for:
- Different male and female appearances.
- Different clothing.
- Different body types.
- Different hairstyles.
- Casual, business, athletic, and workwear outfits.
- Police officers.
- Criminals and suspects.
- Emergency service personnel.

Use directional sprite animations where practical:
- North.
- South.
- East.
- West.
- Diagonal directions where supported.

Include:
- Idle.
- Walking.
- Running.
- Talking.
- Surrendering.
- Resisting.
- Arrested.
- Injured.

### Civilian AI
Implement:
- Sidewalk wandering.
- Pedestrian crossings.
- Avoiding vehicles.
- Basic social behavior.
- Entering and leaving supported buildings.
- Reacting to accidents.
- Reacting to police activity.
- Fleeing and panic behavior.
- Responding to nearby danger.

Civilian behavior should be driven by individual personality profiles and circumstances.

### Personality profiles
Include:
- Cooperative.
- Nervous.
- Confused.
- Uncooperative.
- Aggressive.
- Suspicious.
- Fleeing.

Pedestrians must be connected to persistent citizen records.

## 6. Civilian Vehicle Sprites and Traffic AI

Use top-down vehicle sprites with directional rotation or multiple directional sprite frames.

Include:
- Sedans.
- SUVs.
- Pickup trucks.
- Vans.
- Hatchbacks.
- Sports cars.
- Taxis.
- Delivery vehicles.
- Commercial vehicles.
- Police vehicles.

Vehicles should have different colors, appearances, and handling characteristics.

### Traffic AI
Implement:
- Road network navigation.
- Lane following.
- Intersection handling.
- Traffic light recognition.
- Stop sign behavior.
- Vehicle following distances.
- Collision avoidance.
- Obstacle avoidance.
- Emergency braking.
- Turn signals where visually supported.
- Vehicle spawning and despawning.
- Configurable traffic density.

Civilian traffic must behave naturally and avoid driving through buildings, pedestrians, or other vehicles.

Use a graph-based road navigation system suitable for 2D gameplay.

Do not blindly port 3D vehicle AI systems if they are incompatible with the 2D architecture. Reuse useful logic where appropriate and create 2D-specific implementations.

## 7. Persistent Citizen and Vehicle Database

Create a persistent fictional population using a licensed first-name and surname dataset.

Generate fictional citizen records during initial setup.

Store records locally using SQLite or structured JSON.

Do not regenerate the population on every launch.

Citizen records:
- Unique citizen ID.
- First and last name.
- Date of birth.
- Address.
- Physical description.
- Assigned pedestrian sprite and appearance.
- Driver's license.
- Associated vehicles.
- Criminal history.
- Warrants.
- Citations.
- Arrests.
- Incident history.

Vehicle records:
- Unique vehicle ID.
- Make and model.
- Color.
- Registration and plate.
- VIN-like identifier.
- Registered owner.
- Stolen status.
- Associated records.

All records must persist across sessions.

## 8. Police CAD / MDT

Create a functional police Mobile Data Terminal.

The player should be able to access the MDT through:
- An in-vehicle computer interaction.
- A dedicated keyboard shortcut.
- A controller shortcut.

Display a modern police computer interface.

Include:
- Officer dashboard.
- Officer status.
- Active calls.
- Available callouts.
- Person searches.
- Vehicle searches.
- License checks.
- Registration checks.
- Warrant checks.
- Criminal history.
- BOLO records.
- Stolen vehicle records.
- Incident reports.
- Citation records.
- Arrest records.
- Dispatch history.

Allow the player to create, edit, save, and search incident reports.

The MDT must communicate with the persistent citizen and vehicle databases.

## 9. Pedestrian Police Interactions

Allow the player to approach and interact with pedestrians.

Include:
- Talk to civilian.
- Request identification.
- Ask questions.
- Check identification.
- Issue warning.
- Issue citation.
- Frisk.
- Search where appropriate.
- Detain.
- Arrest.
- Request backup.

Use contextual interaction menus.

Civilian behavior must reflect personality and circumstances.

Interactions must connect to citizen records and dispatch.

## 10. Independent Traffic Stop System

**The traffic-stop system must operate completely independently of the callout framework.**

Traffic stops are a regular free-roam police activity.

The player must be able to conduct traffic stops at any time without accepting a callout.

### Initiating a stop
- Target a civilian vehicle.
- Activate police emergency lights.
- Activate siren if desired.
- Signal the vehicle to pull over.
- Cancel the stop.

### Civilian pull-over AI
The targeted vehicle must:
- Recognize police lights and sirens.
- Identify a safe stopping location.
- Navigate toward the roadside.
- Avoid intersections where possible.
- Avoid blocking traffic.
- Stop smoothly.
- Remain stopped while the interaction is active.

### Driver behavior
Support cooperative, nervous, confused, uncooperative, aggressive, and fleeing drivers.

### Officer interaction
Allow the officer to:
- Approach the vehicle.
- Speak with the driver.
- Request identification.
- Request registration.
- Request insurance.
- Ask questions.
- Explain the reason for the stop.
- Check records.
- Issue a warning.
- Issue a citation.
- Order occupants out.
- Detain occupants.
- Arrest occupants.
- Request backup.

Reuse the pedestrian interaction system for vehicle occupants.

### Database checks
Allow checks for:
- Driver identity.
- License validity.
- Registration.
- Registered owner.
- Vehicle identifier.
- Stolen status.
- Warrants.
- Criminal history.
- BOLOs.

### Outcomes
Support:
- Warning.
- Citation.
- Arrest.
- Search where appropriate.
- Release.
- Passenger detention.
- Driver refusal.
- Driver fleeing.
- Pursuit.
- Backup request.

Save outcomes to police records.

### Fleeing drivers
If a driver flees:
- Start or connect to the pursuit system.
- Notify dispatch.
- Mark the fleeing vehicle on the minimap.
- Allow backup requests.
- Track the vehicle's position.

### Independence requirement
The traffic-stop manager, vehicle interaction logic, and stop lifecycle must not depend on callout classes.

A traffic-stop callout may invoke the independent traffic-stop system through a public interface. Ordinary free-roam traffic stops must work without the callout framework.

## 11. Police Vehicles

Allow the player to:
- Enter and exit police vehicles.
- Drive around the city.
- Switch camera zoom levels.
- Operate emergency lights.
- Operate sirens.
- Use the horn.
- Access the in-vehicle MDT.

Implement appropriate top-down driving mechanics:
- Acceleration.
- Braking.
- Steering.
- Reverse.
- Collision response.
- Vehicle damage where practical.

Police vehicle emergency equipment must be recognized by civilian traffic AI.

## 12. Weapons and Combat

Implement basic top-down weapon mechanics.

Include:
- Draw and holster.
- Aim or directional targeting.
- Shooting.
- Projectile or hitscan detection.
- Recoil where appropriate.
- Ammunition.
- Reloading.
- Damage.
- Weapon animations.
- Weapon audio.

Use directional aiming suitable for a top-down perspective.

Include a weapon inventory and weapon selection interface.

Avoid unnecessary advanced FPS movement mechanics.

## 13. Live Police Radio and In-Game Dispatch Radio

**This is a major gameplay feature and must be implemented as a dedicated, reliable audio system.**

The game must have two independent radio audio sources:

1. Live Police Radio Feed.
2. In-Game Police Dispatch Radio.

They must be managed by a central Radio Audio Manager.

### A. Live Police Radio Feed

Allow the player to listen to a configurable live internet police radio stream while playing.

Implement:
- Configurable stream URL.
- Play and stop.
- Mute and unmute.
- Volume control.
- Station selection.
- Connection status.
- Buffering status.
- Reconnection after connection loss.
- Audio device handling.
- Persistent radio settings.

Provide a settings interface where the player can enter or change a compatible audio stream URL.

Do not hardcode a single radio station.

Use a reliable Unity-compatible streaming approach. Verify codec and streaming protocol compatibility before integration.

Respect the source provider's access rules, licensing, and usage restrictions. Do not scrape private feeds or bypass authentication.

If the stream is unavailable, display a clear status message and allow the game to continue normally.

### B. In-Game Police Dispatch Radio

Create a separate in-game dispatch radio that generates transmissions for:
- Callout offers.
- Active callout updates.
- Traffic stops.
- Arrests.
- Citations.
- Pursuits.
- Backup requests.
- Emergency situations.
- Officer status changes.
- Other police activity.

Include:
- Dispatch voice transmissions.
- Text-to-speech support.
- Pre-recorded voice options where available.
- Configurable radio effects.
- Radio static.
- Compression.
- Equalization.
- Distortion.
- Squelch.
- Transmission queue.
- Dispatch history.
- Text fallback.

Dispatch should be able to speak dynamically using contextual information such as officer location, street names, incident type, and suspect or vehicle descriptions.

### C. Live Feed Ducking and Pause Behavior

The in-game dispatch radio must have priority over the live police radio feed.

When an important dispatch transmission begins, automatically manage the live feed.

Provide three configurable audio modes:

**Mode 1 — Duck**
- Gradually lower the live feed volume.
- Play the in-game dispatch transmission at normal volume.
- Restore live feed volume when dispatch finishes.

**Mode 2 — Pause**
- Pause or suspend the live feed when dispatch starts.
- Play the dispatch transmission.
- Resume the live feed afterward when technically possible.

**Mode 3 — Stop and Reconnect**
- Stop the live stream during dispatch.
- Reconnect to the configured stream after dispatch finishes.
- Use this mode when the streaming provider does not support reliable pause/resume.

The default should be Duck mode.

### D. Audio priority system

Implement transmission priority levels:

1. Emergency / immediate danger.
2. Critical dispatch.
3. Callout announcements and updates.
4. Routine dispatch.
5. Background radio activity.

Allow important transmissions to interrupt lower-priority transmissions.

Routine transmissions should be queued.

Avoid overlapping dispatch voice transmissions.

### E. Live radio controls

Allow the player to:
- Toggle the live feed.
- Change stations.
- Adjust live radio volume.
- Adjust dispatch volume.
- Select Duck, Pause, or Stop/Reconnect mode.
- Mute the live feed without muting dispatch.
- Mute dispatch independently.
- View the current station and connection status.

Provide configurable keyboard and controller controls.

### F. Important streaming limitation

Internet radio streams are not necessarily seekable.

When using Duck mode, the live feed should continue playing underneath the dispatch transmission at reduced volume.

When using Pause mode, preserve a buffer if technically possible. If the source does not support reliable resumption, use the configured reconnect behavior and explain that playback may resume at the current live point rather than the exact point where it was interrupted.

Do not allow streaming errors to crash or freeze gameplay.

### G. Radio system independence

Keep the live streaming subsystem separate from the in-game dispatch subsystem.

The callout framework should send dispatch events through a public interface. It must not directly manipulate audio sources or streaming connections.

Traffic stops and other free-roam gameplay events must also be able to trigger dispatch transmissions without callouts.

## 14. Extensible Callout Framework

Build an extensible callout framework with three independent layers.

### A. Callout Framework
Responsible for:
- Registration.
- Discovery.
- Validation.
- Scheduling.
- Availability.
- Offers.
- Acceptance.
- Cancellation.
- Active callout tracking.
- Objectives.
- Completion.
- Failure.
- Dispatch integration.
- CAD integration.

### B. Individual Callouts
Each callout must be an independent class or module containing its own scenario-specific gameplay logic.

Examples:
- Suspicious person.
- Accident.
- Domestic disturbance.
- Shoplifting.
- Robbery.
- DUI.
- Wanted person.
- Burglary alarm.
- Welfare check.
- Assault.
- Stolen vehicle.
- Reckless driver.
- Weapons complaint.
- Pursuit assistance.
- Traffic stop.

### C. Callout Definitions
Use ScriptableObjects containing metadata and configuration only.

Do not place scenario execution logic in callout definitions.

### Extensibility
Use interfaces such as:
- `ICallout`
- `ICalloutFactory`
- `ICalloutProvider`
- `CalloutPackManifest`

Use Unity assembly definitions where appropriate.

Do not assume unrestricted runtime DLL loading.

A new callout should be addable without editing the core framework, CAD, dispatch, or callout manager.

### Critical independence rule
The traffic-stop system is not owned by the callout framework.

A traffic-stop callout may request a traffic stop through the independent traffic-stop API, but all traffic-stop functionality must work in free roam without any callout being active.

## 15. Dispatch and Police Procedures

Connect dispatch to:
- Callout offers.
- Callout acceptance.
- Callout updates.
- Traffic stops.
- Pursuits.
- Arrests.
- Citations.
- Backup requests.
- Officer emergencies.
- Other police events.

Use realistic police terminology and configurable radio codes.

Include officer status management and dispatch history.

All transmissions should use the central radio priority and audio management system.

## 16. HUD and Minimap

Create a clean HUD inspired by classic GTA.

Include:
- Minimap.
- Player health.
- Vehicle condition.
- Current weapon.
- Ammunition.
- Police status.
- Current callout.
- Interaction prompts.
- Radio status.

### Minimap
Display:
- Player location.
- Player orientation.
- Streets.
- Buildings.
- Callout markers.
- Police stations.
- Points of interest.
- GPS routes.
- Pursuit markers.
- Traffic-stop markers.

Allow configurable zoom and player-rotating or north-up display.

### Radio HUD
Show:
- Live feed status.
- Current station.
- Dispatch activity.
- Transmission priority.
- Volume or mute indicators.

Avoid filling the screen with unnecessary UI.

## 17. Input and Controller Support

Use Unity's Input System.

Support:
- Keyboard and mouse.
- Xbox controllers.
- PlayStation controllers.
- Generic controllers.

Implement action maps for:
- On Foot.
- Vehicle.
- Interaction.
- Weapons.
- Police Computer.
- Police Radio.
- General UI.

Allow key rebinding and save user settings.

All essential gameplay must be possible using either keyboard/mouse or a controller.

## 18. Save System and Settings

Persist:
- Citizen database.
- Vehicle database.
- Incident reports.
- Arrests.
- Citations.
- Warrants.
- BOLOs.
- Player settings.
- Key bindings.
- Radio station URLs.
- Radio volume settings.
- Radio playback preferences.
- Controller bindings.

Do not store temporary streaming connection state as persistent gameplay progress.

Handle missing, invalid, or corrupted save files gracefully.

## 19. Modular Architecture

Use independent systems and clear interfaces for:
- Player controller.
- Camera controller.
- City map.
- Tilemap management.
- Road navigation.
- Pedestrian AI.
- Vehicle AI.
- Citizen database.
- Vehicle database.
- Pedestrian interactions.
- Traffic stops.
- Pursuits.
- Weapons.
- Weapon inventory.
- Police vehicles.
- Live radio streaming.
- Dispatch radio.
- Radio audio manager.
- Callout framework.
- Individual callouts.
- CAD/MDT.
- HUD.
- Minimap.
- Input management.
- Save/load.

Use events, interfaces, and service abstractions.

Avoid giant manager classes.

### Independence rules
- Traffic stops must work without callouts.
- Pedestrian interactions must work without callouts.
- Vehicle AI must work without callouts.
- Dispatch must work without individual callout implementations.
- Live radio streaming must work without dispatch.
- Dispatch must work without live radio streaming.
- Radio audio ducking must be managed centrally.
- Callouts must not directly control the live stream.
- HUD components must not contain core gameplay logic.

## 20. Development and Testing

Implement the project incrementally.

For every feature:
1. Inspect existing systems.
2. Plan integration.
3. Implement.
4. Compile.
5. Test in Unity.
6. Fix errors.
7. Verify existing functionality.
8. Document setup requirements.

### Required acceptance tests

**2D gameplay**
- Player moves around the city.
- Camera follows correctly.
- Vehicles can be driven.
- Pedestrians navigate the city.
- Sprite animations display correctly.
- Buildings and objects sort correctly.

**Traffic**
- Civilian vehicles navigate the road network.
- Traffic lights work.
- Vehicles avoid obstacles.
- Pedestrians use crossings.
- Traffic density can be configured.

**Traffic stops**
- Player can initiate a stop in free roam.
- No callout is required.
- Civilian vehicle safely pulls over.
- Officer can interact with occupants.
- Records checks work.
- Warnings and citations work.
- Arrests work.
- Fleeing drivers trigger pursuit and dispatch.
- Traffic stops work with both input methods.

**CAD and database**
- MDT works.
- Records persist.
- Searches return correct information.
- Reports can be created and edited.

**Callouts**
- Callouts register and appear.
- Offers and acceptance work.
- Callouts can be completed, failed, and cancelled.
- New callouts can be added independently.
- Callouts can trigger dispatch transmissions.

**Live radio**
- Player can enter a compatible stream URL.
- Live feed plays during gameplay.
- Player can change volume and stations.
- Connection loss is handled safely.
- Settings persist between sessions.

**Dispatch radio and ducking**
- Dispatch transmissions play independently of the live stream.
- Duck mode reduces live volume during dispatch.
- Live volume restores smoothly afterward.
- Pause mode pauses and resumes where supported.
- Stop/Reconnect mode reconnects after dispatch.
- Higher-priority transmissions interrupt lower-priority transmissions.
- Routine messages are queued.
- Traffic stops can trigger dispatch without callouts.
- Live radio continues working if dispatch is unavailable.
- Dispatch continues working if internet radio is unavailable.

## 21. Development Priorities

Build in this order:

1. Inspect existing Unity project.
2. Establish 2D project settings and rendering.
3. Build the fixed city map.
4. Implement player movement and camera.
5. Implement civilian pedestrian sprites and AI.
6. Implement civilian vehicle sprites and traffic AI.
7. Implement persistent citizen and vehicle databases.
8. Implement police vehicles.
9. Implement independent traffic stops.
10. Implement pedestrian police interactions.
11. Implement CAD/MDT.
12. Implement dispatch and in-game radio.
13. Implement live police radio streaming and audio ducking.
14. Implement weapons and combat.
15. Implement HUD and minimap.
16. Implement the independent callout framework.
17. Add individual callouts.
18. Optimize and polish the complete game.

The finished product should feel like a modern, feature-rich evolution of classic GTA1, with deep police simulation gameplay and a believable living city.

Do not create placeholder systems when a functional implementation is required. If a feature cannot be completed immediately, implement its architecture correctly and document what remains unfinished.