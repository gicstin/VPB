@echo off
rem Manual broker publish. The plugin build does the same thing on every compile
rem (VPB.csproj -> BuildVpbNetBroker); this is here for publishing the broker on its own,
rem and for copying it into a VaM install in one step.
rem
rem   publish.cmd                -- publish + stage into vam_patch + refresh the manifest
rem   publish.cmd "C:\vam"       -- and copy into that VaM install
rem
rem All of the work lives in scripts\BuildVpbNetBroker.ps1 so the build path and this one
rem cannot drift apart.
setlocal
pushd "%~dp0"

if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "..\..\scripts\BuildVpbNetBroker.ps1" -FailOnError
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "..\..\scripts\BuildVpbNetBroker.ps1" -VamPath "%~1" -FailOnError
)
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
    echo.
    echo Publish failed. See the warnings above.
) else (
    if "%~1"=="" echo Copy it to:  ^<VaM^>\BepInEx\plugins\VpbNet\VpbNet.exe   or re-run: publish.cmd "C:\vam"
)

popd
endlocal
exit /b %RC%
