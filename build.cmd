@echo off
rem Builds build\Reg_Files_Compare.exe (Native AOT, one file) and runs the unit tests.
rem Needs the .NET 11 SDK (rc.2 or later) and Visual Studio 2022 or later (or its Build Tools) with the
rem "Desktop development with C++" workload, whose linker Native AOT uses (BUILD.md). Run it from a plain
rem terminal: the Developer PowerShell garbles the accented profile path and dotnet then fails.
setlocal EnableExtensions

set "ROOT=%~dp0"
set "OUT=%ROOT%build"
set "ASSEMBLY=Reg_Files_Compare"
set "EXE_NAME=%ASSEMBLY%.exe"
set "PUBLISH=%OUT%\publish"
set "EXE=%PUBLISH%\%EXE_NAME%"

rem The exe is published in build\publish\ and moved to build\ once every check and test passed:
rem sign.cmd signs build\Reg_Files_Compare.exe, so a failed or older build must not stay there.
del /q "%OUT%\%EXE_NAME%" >nul 2>&1
if exist "%OUT%\%EXE_NAME%" (
    echo "%OUT%\%EXE_NAME%" cannot be replaced: close it first.
    exit /b 1
)

where dotnet >nul 2>&1 || (
    echo dotnet not found: install the .NET 11 SDK ^(rc.2 or later^).
    exit /b 1
)

rem The unit tests first: a build whose tests fail is not published.
dotnet test "%ROOT%Tests\RegCompare.Tests.csproj" --nologo || exit /b 1

rem Native AOT, one file (RegCompare.csproj). Directory.Build.props keeps UseOptimizedPublish false: the
rem .NET 11 rc.2 SDK's optimized publish skips Avalonia's XAML compiler, and the exe then dies at start.
if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"
dotnet publish "%ROOT%RegCompare.csproj" -c Release -r win-x64 -o "%PUBLISH%" --nologo || exit /b 1
if not exist "%EXE%" (
    echo The publish made no "%EXE_NAME%".
    exit /b 1
)
rem One file: nothing may be left beside the exe (the publish strips the libraries linked into it), or
rem the exe would not run on its own.
dir /b "%PUBLISH%\*.dll" >nul 2>&1 && (
    echo The publish left libraries beside "%EXE_NAME%":
    dir /b "%PUBLISH%\*.dll"
    exit /b 1
)

rem The compiled XAML must be in the exe: its avares:// address (UTF-16, searched without its last zero
rem byte, which the size-optimized AOT image may pack) and the XAML compiler's populate methods.
powershell -NoProfile -Command "$latin = [Text.Encoding]::GetEncoding(28591); $text = $latin.GetString([IO.File]::ReadAllBytes($env:EXE)); $address = [Text.Encoding]::Unicode.GetBytes('avares://' + $env:ASSEMBLY + '/App.axaml'); if ($text.IndexOf($latin.GetString($address, 0, $address.Length - 1), [StringComparison]::Ordinal) -lt 0 -or -not $text.Contains('XamlIlPopulate')) { exit 1 }" || (
    echo "%EXE_NAME%" has no compiled XAML: its window would not start ^(UseOptimizedPublish in Directory.Build.props^).
    exit /b 1
)

move /y "%EXE%" "%OUT%\%EXE_NAME%" >nul || exit /b 1
rmdir /s /q "%PUBLISH%" >nul 2>&1
echo.
echo Built "%OUT%\%EXE_NAME%"
exit /b 0
