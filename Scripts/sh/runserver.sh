#!/bin/sh
python3 mapGeneration.py
dotnet run --project Content.Server
read -p "Press enter to continue"
