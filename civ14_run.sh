#!/bin/sh
git pull
/home/python/bin/pip install numpy pyyaml pyfastnoiselite
/home/python/bin/python mapGeneration.py
dotnet run --project Content.Packaging server --hybrid-acz --platform linux-x64
dotnet run --project Content.Server --config-file server_config.toml
