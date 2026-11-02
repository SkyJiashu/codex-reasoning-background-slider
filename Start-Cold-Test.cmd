@echo off
echo Save your work and completely exit Codex before this test.
echo Existing Codex processes will not be stopped or restarted.
if exist "%~dp0CrystalDragon.exe" (start "" "%~dp0CrystalDragon.exe" --cold-test) else (start "" "%~dp0build\CrystalDragon\CrystalDragon.exe" --cold-test)
