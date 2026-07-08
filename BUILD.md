# Building from source

This is optional. Most users should simply download the prebuilt executable from the
[latest release](https://github.com/Freenitial/Reg_Files_Compare/releases/latest). This document is
for those who want to compile the application themselves.

## Prerequisites

- **Windows 10 version 1809 (build 17763) or later**, 64-bit.
- **.NET 11 SDK.**
- For the single-file `Release` publish only: **Visual Studio 2022 Build Tools** (or the full IDE)
  with the **Desktop development with C++** workload and a **Windows 10/11 SDK**. The NativeAOT
  linker needs the MSVC toolchain. The `Debug` build does **not** require it.

NuGet sources are already configured in [`NuGet.Config`](NuGet.Config): the application depends on an
Avalonia 12.1 build from the Avalonia nightly feed, which `dotnet restore` pulls automatically.

## Get the code

```powershell
git clone https://github.com/Freenitial/Reg_Files_Compare.git
cd Reg_Files_Compare
```

## Build, run and test

```powershell
# Restore + build (Debug)
dotnet build

# Run
dotnet run --project RegCompare.csproj

# Run the test suite
dotnet test
```

## Publish the single-file executable

The NativeAOT publish invokes the MSVC linker, so run it from a **Developer PowerShell for VS 2022**
(its environment puts the C++ toolchain and `vswhere.exe` on `PATH`):

```powershell
dotnet publish RegCompare.csproj -c Release -r win-x64
```

Output:

```
bin\Release\net11.0-windows10.0.17763.0\win-x64\publish\Reg_Files_Compare.exe
```

A standalone, single-file application (~26 MB) that needs no .NET runtime on the target machine.

## Project structure

```
RegCompare.slnx            Solution
RegCompare.csproj          Application project (WinExe, NativeAOT)
Directory.Build.props      Shared compiler settings
Directory.Packages.props   Central package versions
NuGet.Config               Package sources (nuget.org + Avalonia nightly)
App.axaml(.cs), Program.cs Application bootstrap
app.manifest               Win32 application manifest

Assets/                    Application icon
Converters/                Value converters for bindings
Models/                    Domain types and enums
Services/                  .reg parsing, diff engine, drag-and-drop, theme
Styles/                    Theme palette and control styles
ViewModels/                MVVM view-models
Views/                     Windows and views (XAML + code-behind)
Tests/                     xUnit test project
```

## Technical notes

- **MVVM** with [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) source generators.
- **AOT/trim friendly**: no runtime reflection; compiled XAML bindings throughout.
- Rendering uses the software backend; Skia and HarfBuzz are statically linked, producing a single `.exe`.
