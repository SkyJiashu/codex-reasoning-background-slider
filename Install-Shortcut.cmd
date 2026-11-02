@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install-shortcut.ps1"
if errorlevel 1 (
  echo Shortcut setup did not complete. Review the message above.
  pause
  exit /b 1
)
echo The independent Codex - Crystal Dragon shortcut is ready.
pause
