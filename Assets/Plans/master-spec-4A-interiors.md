## 4A. Multi-Story Buildings and Enterable Interiors

The city must contain fully enterable buildings with functional interiors and multiple floors.

Buildings must not be purely decorative exterior sprites. The player should be able to enter selected buildings, explore their interiors, navigate between floors, interact with objects, and encounter pedestrians and suspects inside.

### Building types

Create a variety of enterable buildings, including:
- Apartment buildings.
- Houses.
- Office buildings.
- Police stations.
- Hospitals.
- Shops and convenience stores.
- Restaurants.
- Bars.
- Warehouses.
- Hotels.
- Parking garages.
- Government buildings.
- Commercial buildings.

Different buildings should have distinct layouts, furniture, entrances, and interior appearances.

### Multi-story functionality

Support buildings with multiple accessible floors.

Examples:
- Single-story shops and houses.
- Two-to-four-story apartment buildings.
- Multi-story office buildings.
- Larger apartment complexes.
- Multi-level parking garages.
- Multi-floor police stations and hospitals.

Implement:
- Staircases.
- Elevators where appropriate.
- Hallways.
- Rooms.
- Apartment doors.
- Office doors.
- Interior entrances and exits.
- Floor transitions.

The player must be able to move between floors without loading an entirely separate game or losing gameplay context.

### Interior visibility and closed-door behavior

**Critical visual requirement: The player must NEVER be able to see inside a building through a closed door.**

Implement proper interior visibility management.

When a building entrance or room door is closed:
- The interior must be completely hidden.
- No furniture, characters, lighting, or interior sprites may be visible through the door.
- Do not render the interior through walls, windows, or closed entrances unless a specific window is intentionally transparent.
- Closed doors must visually conceal the space behind them.

When the player opens a door:
- Play an appropriate opening animation.
- Reveal the interior behind the doorway.
- Allow the player to enter.
- Close the door automatically or manually according to the door configuration.

When the player enters a building, activate its interior visibility system.

When the player exits, deactivate or conceal the interior as appropriate.

Use sorting layers, sprite masks, occlusion logic, and visibility groups to ensure the player cannot see through closed doors or solid walls.

### Interior visibility states

Each enterable building should support the following states:

1. **Exterior:** The player is outside. Interior sprites and interior occupants are hidden.
2. **Door opening:** The door animates and the interior becomes visible through the doorway.
3. **Interior active:** The player is inside. The appropriate interior floor and visible rooms are rendered.
4. **Interior occlusion:** Walls and closed doors conceal rooms that the player cannot see into.
5. **Floor transition:** The player changes floors, and the appropriate floor's interior becomes active.
6. **Exit:** The player returns outside, and interior visibility is disabled.

Do not simply make every building interior transparent whenever the player approaches.

### Multi-floor rendering

Use a dedicated floor and visibility system.

Each building should have:
- A unique building ID.
- A list of floors.
- Floor identifiers.
- Interior layouts.
- Door connections.
- Staircase and elevator connections.
- Interior spawn locations.
- Room definitions.
- Visibility and occlusion settings.

Only render the current floor and any specifically visible areas.

The camera must not reveal the interiors of floors above or below the player.

When changing floors:
- Update the active floor.
- Update interior rendering.
- Update character and pedestrian visibility.
- Update navigation.
- Update the minimap.
- Preserve the player's position and gameplay state.

### Doors and access control

Implement functional doors with:
- Open and closed states.
- Opening and closing animations.
- Interaction prompts.
- Collision changes.
- Locked and unlocked states.
- Optional key or access requirements.
- Break-in or forced-entry behavior where appropriate.

Different doors may have different access rules.

Examples:
- Public shops are generally accessible during opening hours.
- Apartment buildings may have locked common entrances.
- Individual apartments may be locked.
- Offices may require access.
- Police-only areas may require authorization.

The player should receive clear feedback when a door is locked or inaccessible.

### Interior furnishing

Populate interiors with appropriate furniture and objects.

Examples:
- Apartments: furniture, kitchens, bedrooms, bathrooms, and living rooms.
- Shops: counters, shelves, registers, storage rooms, and back offices.
- Offices: desks, computers, meeting rooms, and reception areas.
- Police stations: reception, interview rooms, cells, briefing rooms, and offices.
- Hospitals: reception, patient rooms, corridors, and treatment rooms.
- Warehouses: storage racks, loading areas, and offices.

Use reusable 2D interior assets and modular room layouts.

### Interior pedestrians and AI

Pedestrians must be able to exist and move inside buildings.

Implement:
- Walking through hallways.
- Moving between rooms.
- Using stairs.
- Using elevators where supported.
- Entering and leaving buildings.
- Reacting to police entering.
- Fleeing or hiding during incidents.
- Interacting with the player indoors.

Interior pedestrians must connect to the same persistent citizen database used by outdoor pedestrians.

### Interior police gameplay

Callouts and free-roam police activities must support indoor locations.

Examples:
- Responding to a domestic disturbance inside an apartment.
- Investigating a burglary.
- Conducting a welfare check.
- Responding to a robbery inside a shop.
- Searching an office building.
- Pursuing a suspect through an apartment complex.
- Arresting a suspect inside a residence.
- Searching a warehouse.

The player must be able to enter buildings, navigate floors, locate suspects, and complete objectives indoors.

### Building navigation and dispatch

Integrate buildings with the city location database.

Each building should have:
- A street address.
- A building name or type.
- A unique building identifier.
- Floor identifiers.
- Room identifiers where appropriate.
- Enterable door locations.

Dispatch should be able to identify a building and, when relevant, specify a floor or room.

For example:
"Units respond to 125 Main Street, apartment building, third floor."

The minimap should display the player's current building and floor when indoors.

### Performance

Do not keep every building interior fully active simultaneously.

Use:
- Interior activation and deactivation.
- Floor-based rendering.
- Room visibility groups.
- Sprite culling.
- Reusable interior prefabs.
- Efficient collision handling.
- Distance-based pedestrian simulation.

Only load or activate the necessary interior systems.

### Required acceptance tests

- Player can enter and exit a building.
- Closed doors completely conceal interiors.
- Opening a door reveals the interior correctly.
- Walls conceal rooms that should not be visible.
- Player can navigate multiple floors.
- Stairs and elevators connect floors correctly.
- Pedestrians can move between floors.
- Interior pedestrians retain persistent identities.
- Locked doors prevent unauthorized entry.
- Police callouts can take place inside buildings.
- Dispatch can identify building addresses and floors.
- Minimap displays the current building and floor.
- Interior rendering does not expose rooms above or below the player's current floor.

**Do not use fake interior backgrounds or transparent building sprites as a substitute for functional interiors. Build actual navigable interior spaces with proper door, wall, room, and floor visibility logic.**