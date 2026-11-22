@echo off
setlocal
for %%I in ("%~dp0..\..") do set "DIANA_REPO=%%~fI"
pushd "%DIANA_REPO%"
if errorlevel 1 exit /b 1
set "DIANA_RESULTS=%DIANA_REPO%\..\Resultados_Diana_Integracion"
if not exist "%DIANA_RESULTS%" mkdir "%DIANA_RESULTS%"
set "DIANA_RESULT=0"
echo 1/2 Compilando la app para Windows. Las 38 pruebas aprobadas no se repiten.
dotnet build PRE-EMERGENCIAS-APP-MOVIL\PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-windows10.0.19041.0 -m:1 > "%DIANA_RESULTS%\cierre_windows.txt" 2>&1
if errorlevel 1 set "DIANA_RESULT=1"
type "%DIANA_RESULTS%\cierre_windows.txt"
echo.
echo 2/2 Compilando la app para Android.
dotnet build PRE-EMERGENCIAS-APP-MOVIL\PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-android -m:1 > "%DIANA_RESULTS%\cierre_android.txt" 2>&1
if errorlevel 1 set "DIANA_RESULT=1"
type "%DIANA_RESULTS%\cierre_android.txt"
echo.
if "%DIANA_RESULT%"=="0" (echo Ambas compilaciones terminaron correctamente.) else (echo Hay errores pendientes. Los informes quedaron guardados para revisarlos.)
popd
pause
exit /b %DIANA_RESULT%
