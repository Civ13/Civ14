# Mapping
```admonish tip
Also check the SS14 guide.
```

## The TL;DR

Here is a quick rundown of the steps you need to go through to make a new map:

0. Make sure you have the codebase downloaded and compiled. After downloading, run RUN_THIS.py, then compile the game in the tools mode by running the script in Scripts/win/buildall-Tools.bat. Then launch the game using RunAll.bat.

1. Now that you're in game, spawn as an observer. Open the console using `\` and type `mapping 10 Maps/civ/template.yml`. This will open the template map, which has some presets predefined.

2. Expand the map if you need to, first select a floor tile and hold CNTRL and drag it to expand. Remember to enclose the new area in indestructible walls, normally WallRockIndestructible. If you do so, remember to run `fixgridatmos` in the console.

## Tutorial - your first map
