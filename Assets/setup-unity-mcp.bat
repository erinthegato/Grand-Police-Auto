@echo off
setlocal
set "PROJ=C:\Users\joshy\Grand Police Auto"

echo === Step 1: Unity CLI ===
where unity >nul 2>nul
if errorlevel 1 (
  echo Installing Unity CLI...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://unity.com/install.ps1 | iex"
  set "PATH=%PATH%;%USERPROFILE%\.unity\bin;%LOCALAPPDATA%\Programs\unity\bin"
)
where unity >nul 2>nul
if errorlevel 1 (
  echo.
  echo Unity CLI installed, but this window can't see it yet.
  echo Close this window and double-click this file again.
  pause
  exit /b 1
)
unity --version

echo.
echo === Step 2: Add Pipeline package to the project ===
unity pipeline install --project-path "%PROJ%"

echo.
echo === Step 3: Connect the MCP to the Claude desktop app ===
unity mcp configure claude --project-path "%PROJ%" --yes

echo.
echo === Step 4: Check the connection (Unity must be open for "ready") ===
unity status --project-path "%PROJ%"

echo.
echo Done! Fully quit Claude and reopen it to load the Unity MCP.
echo You can delete this file afterwards.
pause
