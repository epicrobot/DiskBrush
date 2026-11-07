@echo off
rem Builds DiskBrush with the C# compiler that ships with Windows. No downloads.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin

set FLAGS=/nologo /warnaserror /optimize+ /platform:anycpu

"%CSC%" %FLAGS% /target:exe /out:bin\ScannerTest.exe /recurse:src\Core\*.cs /recurse:tests\*.cs
if errorlevel 1 goto failed
copy /y App.config bin\ScannerTest.exe.config >nul

"%CSC%" %FLAGS% /target:winexe /win32icon:diskbrush.ico /out:bin\DiskBrush.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /recurse:src\Core\*.cs /recurse:src\Gui\*.cs
if errorlevel 1 goto failed
copy /y App.config bin\DiskBrush.exe.config >nul

echo Build OK
exit /b 0

:failed
echo BUILD FAILED
exit /b 1
