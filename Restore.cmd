@echo off
if exist "%~dp0CrystalDragon.exe" (start "" "%~dp0CrystalDragon.exe" --restore) else (start "" "%~dp0build\CrystalDragon\CrystalDragon.exe" --restore)
