# Mapping

```admonish tip
Also check the SS14 guide.
```

```admonish info
Make sure you have the codebase downloaded and compiled. After downloading, run RUN_THIS.py, then compile the game in the tools mode by running the script in Scripts/win/buildall-Tools.bat. Then launch the game using RunAll.bat.
```

## The TL;DR

Here is a quick rundown of the steps you need to go through to make a new map, in case you need a refresher or you're already familiar with the mapping process. Otherwise, scroll down to the tutorial.

1. When you're ingame, spawn as an observer. Open the console using `\` and type `mapping 10 Maps/civ/template.yml`. This will open the template map, which has some presets predefined.

2. Expand the map if you need to, first select a floor tile and hold CTRL and drag it to expand. Remember to enclose the new area in indestructible walls, normally `WallRockIndestructible`. If you do so, remember to run `fixgridatmos` in the console.

3. Map to your heart's content. Don't forget to add:

    - Spawns for all factions
    - An observer spawn somewhere
    - Capturable area(s), if applicable
    - Grace walls

4. Save the map using `savemap 10 mapname.yml`. The file will be created in `bin/Content.Server/Data`. Move it to the map folder in Resources/ afterwards.

5. Create a new map metadata in `Resources/Prototypes/Maps` and edit it accordingly. Don't forget to define an ID, the preset and the jobs.

6. Edit the map file and add the ID you defined above to the station section.

7. **Test it!!**

## Tutorial - Your First Map

Let’s create a simple WW2-themed **King of the Hill (KotH)** map called **River**, set in a European theater with two factions (Germans and Soviets) fighting to control a central river crossing.

### Step 1: Setup and Template

Ensure the Civ14 codebase is compiled (see the note on the top of this page). Copy the template map file `Resources/Maps/basic.yml.template` and rename it `river.yml`. This template includes basic presets like weather and global lights.

Launch the game using `RunAll.bat`, spawn as an observer, and open the console (`\`). Type: `mapping 10 Maps/civ/river.yml`

This loads the `river.yml` map in the editor and spawns you as an observer, where you can edit the map.

### Step 2: Expand the Map

The template map is a small grid, so let’s expand it for a KotH layout:

1. **Expand the Grid**: Select a floor tile (e.g., `FloorPlanetGrass`), hold `CTRL`, and drag to expand the map to a reasonable size (e.g., 100x100 tiles for a medium map). Ensure the terrain reflects a WW2 battlefield—use grass, dirt, and river tiles (`FloorWaterEntity` and `FloorWaterDeepEntity`).
2. **Enclose the Area**: Surround the map with indestructible walls (`WallRockIndestructible`, using the entity spawner) to prevent players from falling into space.
3. **Fix Atmosphere**: Run `fixgridatmos` in the console to stabilize the map’s atmosphere, ensuring no air leaks or physics issues.

```admonish warning
Make sure the map is fully enclosed, otherwise oxygen will escape and players will suffocate to death!
```

### Step 3: Design the Terrain

```admonish tip
Mapping is trial and error! Save often, test frequently, and check the Civ14 GitHub for community feedback or new mapping tools.
```

Shape the **River** map to fit its WW2 KotH theme:

-   **Central Objective**: Place a capturable marker in the map’s center using `MarkerKOTHOpushka`. This is from the Opushka map, but it will work just fine and is already preconfigured for the German and Soviet factions, and to have a radius of 5. You can have a look at it in `Resources/Prototypes/Civ14/Entities/Markers/capturable_areas.yml.
-   **River**: Add a winding river (`FloorWaterDeep`) splitting the map, with the bridge as the only crossing point. Use shallow water tiles (`FloorWaterShallow`) for fording. Note that water is an object, not a tile! You will need to make sure you do not accidentally double the ammount of water entities in a tile.
-   **Terrain Features**: Scatter trees (`TreeTemperate`, for example), Rocks (`WallRock`) and bushes (found under decals) to give the map some cover and flavour.

### Step 4: Add Key Elements

Include essential entities for gameplay:

-   **Faction Spawns**:
    -   Germans: Place 10 or so `SpawnPointGerman` in the left side of the map near the wall.
    -   Soviets: Place 10 or so `SpawnPointSoviet` in the right side of the map.
-   **Observer Spawn**: Add a single `SpawnPointObserver` in a neutral corner (e.g., map edge) or in the middle of the map for spectators.
-   **Grace Walls**: Place temporary `GraceWall` entities around each faction’s base to prevent early rushes. Add `GraceWallGerman` around the Soviet base and `GraceWallSoviet` around the Soviet base. Also add `GraceWall` on the inside of the previous grace walls. Press `Toggle BB` on the sandbox menu to see its boundaries and make sure there are no holes.

### Step 5: Save the Map

Save your work by typing in the console: `savemap 10 river.yml`

The file will save to `bin/Content.Server/Data`. Move it to `Resources/Maps/civ/tdm/` afterward.

### Step 6: Create Map Metadata

Create a new file in `Resources/Prototypes/Maps/river.yml` to define the map’s metadata, specifying its ID, name, path, jobs, factions, and gamemode. Based on Civ14’s structure (similar to the **Camp** map metadata), here’s the metadata for the **River** map:

```yaml
- type: gameMap
  id: River
  mapName: "River (WW2)"
  mapPath: /Maps/civ/tdm/river.yml
  minPlayers: 0
  maxPlayers: 50
  maxRandomOffset: 0
  randomRotation: false
  fixedPreset: TDM
  stations:
      River:
          stationProto: StandardStationTDM
          components:
              - type: StationNameSetup
                mapNameTemplate: "River (WW2)"
              - type: StationJobs
                availableJobs:
                    # German
                    GermanCaptain: [1, 1]
                    GermanSergeant: [4, 4]
                    GermanSubmachineGunner: [10, 10]
                    GermanRifleman: [40, 40]
                    GermanMachinegunner: [5, 5]
                    GermanMedic: [3, 3]

                    # Soviet
                    SovietCaptain: [1, 1]
                    SovietSergeant: [4, 4]
                    SovietSubmachineGunner: [10, 10]
                    SovietRifleman: [40, 40]
                    SovietMachinegunner: [5, 5]
                    SovietMedic: [3, 3]
```

Metadata Notes:

-   id and mapName: Set to River and River (WW2) for clarity.
-   mapPath: Points to `/Maps/civ/tdm/river.yml`, or wherever your map is.
-   minPlayers and maxPlayers: Set to 0 and 50 to support small-to-medium KotH matches, adjustable based on map size.
-   availableJobs: Includes WW2-appropriate roles with counts similar to Opushka (e.g., 1 commander, 15 soldiers per faction). fixedPreset: Specifies KotH victory conditions (4-minute capture) of a common point.
-   Checking Metadata: Ensure job IDs (e.g., SovietMachinegunner) exist in `Resources/Prototypes/Civ14/Roles/Jobs/TDM/`.
-   Common Issues: Incorrect mapPath or undefined job IDs can prevent loading. If the map crashes, check logs in bin/Content.Server/Data/logs or validate YAML syntax.

### Step 7: Update the Map File

Open river.yml in a text editor and add the map ID to the metadata and station sections, use CTRL-F to find them:

```yaml
entities:
    - proto: ""
      entities:
          - uid: 1
            components:
                - type: MetaData
                  name: River (WW2)
```

and

```yaml
- uid: 2
  components:
      - type: MetaData
        name: River (WW2)
```

and

```yaml
- type: BecomesStation
  id: River
```

Ensure the id matches the metadata’s id field (`River`) and the name aligns with mapNameTemplate.

### Step 8: Test the Map

Open `Resources/ConfigPresets/Build/development.toml` and change the value of **map** to **River** and the value of **defaultpreset** to **tdm**. This will make the game load the map automatically. Run the game.

Test the following:

-   Spawns: Ensure Allies and Axis spawn correctly in their bases.
-   Capture Point: Verify the bridge's capture point object tracks control times.
-   Grace Walls: Confirm they have no holes in them.
-   Terrain: Check pathfinding (e.g., no blocked routes) and atmosphere stability (no air leaks).
-   Metadata: Confirm jobs load correctly and the KotH gamemode activates with the 4-minute capture condition.
-   Balance: Playtest with bots or players to ensure the bridge isn’t too easy/hard to hold.Fix issues by tweaking the map in the editor or editing the metadata file, then resave.
