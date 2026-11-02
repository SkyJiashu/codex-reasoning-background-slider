@echo off
if exist "%~dp0CrystalDragon.exe" (start "" "%~dp0CrystalDragon.exe" --preview) else (start "" "%~dp0build\CrystalDragon\CrystalDragon.exe" --preview)
