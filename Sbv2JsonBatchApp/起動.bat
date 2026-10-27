@echo off
cd /d "%~dp0"
"J:\system_tools\venvs\py310-common\Scripts\python.exe" sbv2_batch.py
if errorlevel 1 pause
