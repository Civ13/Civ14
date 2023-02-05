<p align="center"> <img alt="Civilization 14" width="60%" src="https://raw.githubusercontent.com/taislin/civ14/master/Resources/Textures/Logo/splash.png" /></p>

Civilization 14 is a port of [Civilization 13](https://github.com/Civ13/civ13) from [BYOND](https://byond.com) to [Robust Toolbox](https://github.com/space-wizards/RobustToolbox), used for Space Station 14.

**Forked from https://github.com/space-wizards/space-station-14/commit/bd4bbec3d532d7824c6293e4469b9c31a856c3b9**

## Links

[Website](https://civ13.com/) | [Discord](https://discord.gg/hBEtg4x) | [SS14 Hub](https://spacestation14.io/about/nightlies/)

## Building

1. Make sure to install [.NET 7](https://dotnet.microsoft.com/en-us/download)) and [Python 3](https://www.python.org/downloads/).
2. Clone this repository.
3. Open the folder `BuildChecker` and run the `git_helper.py` file to init submodules and download the engine. Use the console to run it (it will be `py -3 git_helper.py` or `python git-helper.py` or `python3 git-helper.py`)
4. Run `testrun.bat` to compile and launch the client and server.

More detailed instructions on building the project can be found on the SS14 docs, [here](https://docs.spacestation14.io/getting-started/dev-setup).
