@echo off
rem Entry point of the self-extracting setup (IExpress launches this file, cwd = extraction dir).
rem Batch files must be plain ASCII with CRLF line endings - cmd.exe misparses anything else.
rem Arguments are passed straight through, e.g. -Silent -Dir "C:\Games\Terraforming Mars".
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
exit /b %ERRORLEVEL%
