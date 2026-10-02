@echo off
rem Abbild check: captures fixed screens into the "snapshots" folder (1-2 min). Does not touch your save data.
cd /d "%~dp0"
if exist snapshots rmdir /s /q snapshots
start "" /wait Abbild.exe --snapshots snapshots --data snapshots\data --windowed
echo exit=%ERRORLEVEL% > snapshots\exitcode.txt
echo Done. See the "snapshots" folder.
