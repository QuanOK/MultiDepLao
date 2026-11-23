@echo off
rem Bam dup: chon ban cai dat / portable -> tao zip gui khach trong dist\gui-khach (thu muc goc MultiDepLao\).
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0gui-khach.ps1" %*
echo.
pause
