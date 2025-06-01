# Mapping
```admonish tip
Also check the SS14 guide.
```

## The TL;DR

Here is a quick rundown of the steps you need to go through to make a new map, in case you need a refresher or you're already familiar with the mapping process. Otherwise, scroll down to the tutorial.

0. Make sure you have the codebase downloaded and compiled. After downloading, run RUN_THIS.py, then compile the game in the tools mode by running the script in Scripts/win/buildall-Tools.bat. Then launch the game using RunAll.bat.

1. Now that you're in game, spawn as an observer. Open the console using `\` and type `mapping 10 Maps/civ/template.yml`. This will open the template map, which has some presets predefined.

2. Expand the map if you need to, first select a floor tile and hold CTRL and drag it to expand. Remember to enclose the new area in indestructible walls, normally WallRockIndestructible. If you do so, remember to run `fixgridatmos` in the console.

3. Map to your heart's content. Don't forget to add:
  - Spawns for all factions
  - An observer spawn somewhere
  - Capturable area(s), if applicable
  - Grace walls

4. Save the map using `savemap 10 mapname.yml`. The file will be created in `bin/Content.Server/Data`. Move it to the map folder in Resources/ afterwards.

5. Create a new map metadata in `Resources/Prototypes/Maps` and edit it accordingly. Don't forget to define an ID, the preset and the jobs.

6. Edit the map file and add the ID you defined above to the station section.

7. **Test it!!**

## Tutorial - your first map

Let's create a simple WW2, King of the hill map. We will name it River.

Make sure you have the game compiled, check step 0 above.

First, copy the template map file, `basic.yml.template` and name it river.yml. Open the game on whatever default map you have and spawn as an observer. Open the console using `\` and type `mapping 10 river.yml`.


