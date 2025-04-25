# Civilization 14

<p align="center"> <img alt="Civilization 14" width="60%" src="https://raw.githubusercontent.com/taislin/civ14/master/Resources/Textures/Logo/splash.png" /></p>

Civilization 14 is a port of [Civilization 13](https://github.com/Civ13/civ13) from [BYOND](https://byond.com) to [Robust Toolbox](https://github.com/space-wizards/RobustToolbox), used for Space Station 14, which itself is a remake of SS13.

## Links

[Civ14 Website](https://civ13.com/) | [Civ14 Discord](https://discord.gg/hBEtg4x) | [Civ14 Wiki](https://civ13.github.io/Civ14) | [SS14 Steam](https://store.steampowered.com/app/1255460/Space_Station_14/) | [SS14 Standalone Download](https://spacestation14.io/about/nightlies/)

## Documentation/Wiki

SS14's [docs site](https://docs.spacestation14.io/) has documentation on SS14s content, engine, game design and more.

## Contributing

We are happy to accept contributions from anybody. Get in Discord if you want to help. We've got a [list of issues](https://github.com/civ13/civ14/issues) that need to be done and anybody can pick them up. Don't be afraid to ask for help either!

## Building

1. Clone this repo.
2. Run `RUN_THIS.py` to init submodules and download the engine.
3. Compile the solution. You can use `Scripts/bat/runclient.bat` and `Scripts/bat/runserver.bat` to launch it locally for testing on Windows (use the scripts in `Scripts/sh/` for linux).

[More detailed instructions on building the project.](https://docs.spacestation14.com/en/general-development/setup.html)

## License

All code for the content repository is licensed under [MIT](LICENSE.TXT). This includes both the original SS14 content forked from their [official repository](https://github.com/space-wizards/space-station-14), included in this [squashed commit](https://github.com/Civ13/Civ14/commit/c1bd8faf8e9bf856c6cc0d7f0b2baf0ea6769326), and the code contributed to this repository after that.

Most assets are licensed under [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) unless stated otherwise. Assets have their license and the copyright in the metadata file. [Example](Resources/Textures/Objects/Tools/crowbar.rsi/meta.json).

Note that some assets are licensed under the non-commercial [CC-BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/) or similar non-commercial licenses and will need to be removed if you wish to use this project commercially.

Assets ported over from Civ13 are licenced under [GNU Affero General Public License v3.0 (AGPLv3)](https://opensource.org/license/agpl-v3). Check the `meta.json` files or the `attributions.txt` files accompaniying them.
