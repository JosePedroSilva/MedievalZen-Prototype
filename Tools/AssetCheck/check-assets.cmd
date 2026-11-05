@echo off
rem Runs the MedievalZen asset check with Windows PowerShell, bypassing the execution policy for this one script only.
rem Usage: Tools\AssetCheck\check-assets.cmd [-ProjectRoot <dir>] [-Manifest <file>] [-SkipLfs]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0check-assets.ps1" %*
exit /b %ERRORLEVEL%
