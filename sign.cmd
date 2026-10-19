@echo off
rem Signs build\Reg_Files_Compare.exe (made by build.cmd) with the release code signing certificate,
rem then timestamps it so the signature outlives the certificate. The certificate's key stays on its
rem hardware token: plug it in, then give its PIN when the token's software asks (this script never
rem sees it). When several valid certificates are issued to SIGNER, set SIGN_THUMBPRINT to the one
rem to use.
setlocal EnableExtensions

rem The certificate's subject common name: only a certificate issued to exactly this name signs.
set "SIGNER=Leo Antonin Gillet"
rem The timestamp server of the certificate's authority (RFC 3161).
set "TIMESTAMP=http://ts.harica.gr"
rem What the signature names: the program and its page.
set "PRODUCT=REG Files Compare"
set "REPOSITORY=Freenitial/Reg_Files_Compare"

set "ROOT=%~dp0"
set "EXE=%ROOT%build\Reg_Files_Compare.exe"
if not exist "%EXE%" (
    echo "%EXE%" not found: run build.cmd first.
    exit /b 1
)

rem signtool.exe comes with the Windows SDK, which the C++ workload installs.
set "ARCH=x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "ARCH=arm64"
set "SIGNTOOL="
call :FindSignTool "%ProgramFiles(x86)%\Windows Kits\10"
if not defined SIGNTOOL for /f "tokens=2,*" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows Kits\Installed Roots" /v KitsRoot10 /reg:32 2^>nul') do (
    if /i "%%a"=="REG_SZ" call :FindSignTool "%%b"
)
if not defined SIGNTOOL (
    echo signtool.exe not found: install the Windows SDK ^(Visual Studio's C++ workload has it^).
    exit /b 1
)

rem The certificate, chosen before signing: in the current user's store (where the token's
rem certificate appears while it is plugged in), with its private key, valid today, for code
rem signing, issued to exactly SIGNER; exactly one, or the one SIGN_THUMBPRINT names.
set "THUMBPRINT="
for /f "delims=" %%t in ('powershell -NoProfile -Command "$now = Get-Date; $store = New-Object Security.Cryptography.X509Certificates.X509Store('My', 'CurrentUser'); $store.Open('ReadOnly'); $found = @($store.Certificates | Where-Object { $usages = @($_.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value }); $_.HasPrivateKey -and $_.NotBefore -le $now -and $_.NotAfter -gt $now -and $usages -contains '1.3.6.1.5.5.7.3.3' -and [string]::Equals($_.GetNameInfo('SimpleName', $false), $env:SIGNER, [StringComparison]::Ordinal) }); $store.Close(); if ($env:SIGN_THUMBPRINT) { $wanted = ($env:SIGN_THUMBPRINT -replace '\s', '').ToUpperInvariant(); $found = @($found | Where-Object { $_.Thumbprint -eq $wanted }) }; if ($found.Count -eq 1) { $found[0].Thumbprint } elseif ($found.Count -gt 1) { 'SEVERAL: ' + (($found | ForEach-Object { $_.Thumbprint + ' until ' + $_.NotAfter.ToString('yyyy-MM-dd') }) -join ', ') }"') do set "THUMBPRINT=%%t"
if not defined THUMBPRINT (
    if defined SIGN_THUMBPRINT (
        echo No valid code signing certificate issued to "%SIGNER%" has the thumbprint SIGN_THUMBPRINT gives.
    ) else (
        echo No valid code signing certificate issued to "%SIGNER%" with its private key: is the token plugged in?
    )
    exit /b 1
)
if "%THUMBPRINT:~0,8%"=="SEVERAL:" (
    echo Several certificates are issued to "%SIGNER%": set SIGN_THUMBPRINT to one of them.
    echo %THUMBPRINT:~9%
    exit /b 1
)
echo Signing with the certificate issued to "%SIGNER%" ^(%THUMBPRINT%^)

"%SIGNTOOL%" sign /sha1 %THUMBPRINT% /fd sha256 /d "%PRODUCT%" /du "https://github.com/%REPOSITORY%" "%EXE%" || (
    echo Signing failed: is the token plugged in, and was its PIN given?
    exit /b 1
)

rem The timestamp is asked separately: it needs no key (so no PIN), and a timestamp server that is
rem briefly down is tried again, 4 times in all.
set /a ATTEMPT=0
:Timestamp
set /a ATTEMPT+=1
"%SIGNTOOL%" timestamp /tr "%TIMESTAMP%" /td sha256 "%EXE%" && goto :Verify
if %ATTEMPT% GEQ 4 (
    echo Timestamping failed 4 times: "%EXE%" is signed but has no timestamp. Run sign.cmd again later.
    exit /b 1
)
set /a WAIT=5*ATTEMPT
echo The timestamp server did not answer: trying again in %WAIT% seconds.
powershell -NoProfile -Command "Start-Sleep -Seconds %WAIT%"
goto :Timestamp

:Verify
rem /tw: a signature without its timestamp would stop verifying when the certificate expires.
"%SIGNTOOL%" verify /pa /tw /q "%EXE%" || (
    echo The signature does not verify, or it has no timestamp.
    exit /b 1
)
rem The file must carry the chosen certificate. .NET rather than Get-AuthenticodeSignature, whose
rem module fails to load when PowerShell 7 started this.
powershell -NoProfile -Command "$signer = [Security.Cryptography.X509Certificates.X509Certificate2][Security.Cryptography.X509Certificates.X509Certificate]::CreateFromSignedFile($env:EXE); if ($signer.Thumbprint -ne $env:THUMBPRINT) { Write-Output ('Signed by ' + $signer.Subject + ', not by the chosen certificate.'); exit 1 }" || (
    "%SIGNTOOL%" remove /s /q "%EXE%" && (
        echo The signature was removed: sign again.
    ) || (
        echo The signature could not be removed: delete "%EXE%" and build again.
    )
    exit /b 1
)

echo.
echo Signed and timestamped "%EXE%"
exit /b 0

rem Sets SIGNTOOL to the signtool.exe of the newest Windows 10 SDK under the Windows Kits root %1, for
rem this machine's architecture (ARCH). The SDK folders are named 10.0.<build>.0 with a 5-digit build,
rem so their alphabetical order is their version order.
:FindSignTool
set "KITS=%~1"
if "%KITS:~-1%"=="\" set "KITS=%KITS:~0,-1%"
for /f "delims=" %%v in ('dir /b /ad /on "%KITS%\bin\10.*" 2^>nul') do (
    if exist "%KITS%\bin\%%v\%ARCH%\signtool.exe" set "SIGNTOOL=%KITS%\bin\%%v\%ARCH%\signtool.exe"
)
exit /b 0
