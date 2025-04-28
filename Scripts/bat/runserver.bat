@echo off
cd..
cd..
python3 mapGeneration.py
dotnet run --project Content.Server
pause
