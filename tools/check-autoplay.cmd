@echo off
rem Abbild check: plays with random input for 3 minutes to make sure nothing crashes.
cd /d "%~dp0"
if exist autoplay rmdir /s /q autoplay
start "" /wait Abbild.exe --autoplay 180 --snapshots autoplay --data autoplay\data --windowed
echo exit=%ERRORLEVEL% > autoplay\exitcode.txt
