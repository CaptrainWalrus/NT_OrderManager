@echo off
echo Removing kill switch file...
del C:\temp\kill_backtest.txt 2>nul
echo Kill switch removed. Backtest can run normally.
pause 