@echo off
if exist "%~dp0CrystalDragon.exe" (start "" "%~dp0CrystalDragon.exe" --entrypoints) else (start "" "%~dp0build\CrystalDragon\CrystalDragon.exe" --entrypoints)
