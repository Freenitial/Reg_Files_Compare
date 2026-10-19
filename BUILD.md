# Building from source

This is optional. Most users should simply download the prebuilt, signed executable from the
[latest release](https://github.com/Freenitial/Reg_Files_Compare/releases/latest). This document is
for those who want to compile the application themselves.

## Prerequisites

- **Windows 10 version 1809 (build 17763) or later**, 64-bit.
- **.NET 11 SDK** (rc.2 or later).
- For the single-file `Release` publish only: **Visual Studio Build Tools 2022 or later** (or the full
  IDE) with the **Desktop development with C++** workload and a **Windows 10/11 SDK**: Native AOT links
  with the MSVC linker. The `Debug` build does **not** need it.

All packages come from nuget.org ([`NuGet.Config`](NuGet.Config)): Avalonia 12.1.4 (stable) and
`2ndLAB.SkiaSharp.Static`, the static Skia/HarfBuzz libraries linked into the executable.

## Get the code

```powershell
git clone https://github.com/Freenitial/Reg_Files_Compare.git
cd Reg_Files_Compare
```

## Build, run and test

```powershell
# Restore + build (Debug)
dotnet build

# Run (optionally with .reg files or folders to load and compare at once)
dotnet run --project RegCompare.csproj -- a.reg b.reg

# Run the test suite
dotnet test
```

## Publish the single-file executable

```powershell
dotnet publish RegCompare.csproj -c Release -r win-x64
```

Output:

```
bin\Release\net11.0-windows10.0.17763.0\win-x64\publish\Reg_Files_Compare.exe
```

A standalone, single-file application (~28 MB) that needs no .NET runtime on the target machine. The
.NET SDK finds the MSVC linker by itself (through `vswhere.exe`), so a plain terminal works; a Developer
PowerShell is not needed — and better avoided: with a user profile path that has accents, it garbles the
path and dotnet fails.

> The published file is `Reg_Files_Compare.exe` — upload it as the GitHub release asset under that exact
> name (the README's download link points to it).

### Check the compiled XAML

The window only starts if Avalonia's XAML compiler rewrote the assembly before the AOT compiler read
it. The .NET 11 rc.2 SDK publishes Native AOT "optimized" by default (Compile only, no Build) and skips
that step: the exe comes out several MB smaller and dies at start ("No precompiled XAML found for
RegCompare.App"). `UseOptimizedPublish=false` in [`Directory.Build.props`](Directory.Build.props)
prevents it, and `build.cmd` checks the result. To check a published exe by hand (both lines must say
`True`):

```powershell
$exe  = (Resolve-Path 'bin\Release\net11.0-windows10.0.17763.0\win-x64\publish\Reg_Files_Compare.exe').Path
$text = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($exe))
$address = [Text.Encoding]::GetEncoding(28591).GetString([Text.Encoding]::Unicode.GetBytes('avares://Reg_Files_Compare/App.axaml'))
'App.axaml: {0}' -f $text.Contains($address.Substring(0, $address.Length - 1))
'XamlIlPopulate: {0}' -f $text.Contains('XamlIlPopulate')
```

Then start the exe: the window must open and stay open.

### Troubleshooting

- `vswhere.exe not found`: no Visual Studio (nor its Build Tools) is installed. Install the Visual Studio
  Build Tools with the **Desktop development with C++** workload.
- `vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64`: the
  **Desktop development with C++** workload is missing. Add it with the Visual Studio Installer.
- A transient `MSB3061` "access denied" on the output `.exe` is usually an antivirus briefly locking the
  freshly written file: run the publish again.

## Release: build.cmd and sign.cmd

Releases are built and signed on the maintainer's machine: the code signing key lives on a hardware
token. From a terminal in the repository folder, with the token plugged in:

```powershell
# Run the unit tests, publish the Native AOT exe, check it, then put it in build\
.\build.cmd

# Sign build\Reg_Files_Compare.exe and timestamp it (the token's software asks for its PIN)
.\sign.cmd
```

`build.cmd` stops at the first failure (dotnet missing, the previous exe still open, a failing test, a
failed publish, a library left beside the exe, an exe without its compiled XAML) and then leaves no `Reg_Files_Compare.exe` in `build\`: the exe
is put there only once everything passed.

`sign.cmd`:

1. chooses the certificate before signing: in the current user's store, with its private key, valid
   today, for code signing, whose subject common name is exactly "Leo Antonin Gillet". When several
   match, set `SIGN_THUMBPRINT` to the one to use;
2. signs with `signtool sign /sha1 <thumbprint> /fd sha256`, with the description "REG Files Compare"
   and the GitHub page;
3. timestamps separately (`signtool timestamp /tr http://ts.harica.gr /td sha256`): this step needs no
   key, so it is tried up to four times in all, without asking for the PIN again;
4. verifies the signature and its timestamp (`signtool verify /pa /tw`) and checks that the file
   carries the chosen certificate (it removes the signature otherwise).

`signtool.exe` comes with the Windows SDK of the C++ workload. Neither script holds a secret: the PIN
is typed in the token's own dialog.

## Project structure

```
RegCompare.slnx            Solution
RegCompare.csproj          Application project (WinExe, Native AOT)
Directory.Build.props      Shared compiler settings
Directory.Packages.props   Central package versions
NuGet.Config               Package source (nuget.org)
App.axaml(.cs), Program.cs Application bootstrap
app.manifest               Win32 application manifest
build.cmd, sign.cmd        Release build and signing

Assets/                    Application icon, release-note logo
Converters/                Value converters for bindings
docs/                      help.html (embedded in the exe, opened by Help and F1), README screenshot
Models/                    Domain types and enums
Services/                  .reg parsing, diff engine, export, drag-and-drop, theme, help
Styles/                    Theme palette and control styles
ViewModels/                MVVM view-models
Views/                     Windows and views (XAML + code-behind)
Tests/                     xUnit tests (the main window is driven without a screen by Avalonia.Headless)
```

## Technical notes

- **MVVM** with [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) source generators.
- **AOT/trim friendly**: no reflection-based bindings; compiled XAML bindings throughout.
- Rendering is done in software (the renderer uses no GPU); Skia and HarfBuzz are statically linked,
  producing a single `.exe`.
- The documentation (`docs/help.html`, English and French) is a self-contained page embedded in the exe
  (`EmbeddedResource`); the Help button writes it to `%TEMP%\Reg_Files_Compare\` and opens it in the
  default browser. Its technical reference describes how the application works.
