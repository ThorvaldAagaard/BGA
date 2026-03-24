@echo off
set PATH=%PATH%;C:\Program Files (x86)\Microsoft Visual Studio\Installer
call "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\Tools\VsDevCmd.bat" -arch=amd64
cd /d D:\GitHub\BGA\BGADLL.Native
dotnet publish BGADLL.Native.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
