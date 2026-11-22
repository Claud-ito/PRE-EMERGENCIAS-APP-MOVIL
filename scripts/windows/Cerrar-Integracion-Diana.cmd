@echo off
setlocal
for %%I in ("%~dp0..\..") do set "DIANA_REPO=%%~fI"
pushd "%DIANA_REPO%"
if errorlevel 1 exit /b 1
set "DIANA_RESULTS=%DIANA_REPO%\..\Resultados_Diana_Integracion"
if not exist "%DIANA_RESULTS%" mkdir "%DIANA_RESULTS%"
set "UNIVALLE_TEST_SQLSERVER=Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True"
set "DIANA_RESULT=0"
echo 1/3 Pruebas de integracion y SQL Server. Se usan bases temporales propias.
dotnet test tests\Univalle.Emergencias.Tests\Univalle.Emergencias.Tests.csproj -m:1 --logger "trx;LogFileName=Cierre_Diana_Said.trx" --results-directory "%DIANA_RESULTS%" > "%DIANA_RESULTS%\cierre_sql.txt" 2>&1
if errorlevel 1 set "DIANA_RESULT=1"
type "%DIANA_RESULTS%\cierre_sql.txt"
echo.
echo 2/3 Compilacion de la app para Windows.
dotnet build PRE-EMERGENCIAS-APP-MOVIL\PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-windows10.0.19041.0 -m:1 > "%DIANA_RESULTS%\cierre_windows.txt" 2>&1
if errorlevel 1 set "DIANA_RESULT=1"
type "%DIANA_RESULTS%\cierre_windows.txt"
echo.
echo 3/3 Compilacion de la app para Android.
dotnet build PRE-EMERGENCIAS-APP-MOVIL\PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-android -m:1 > "%DIANA_RESULTS%\cierre_android.txt" 2>&1
if errorlevel 1 set "DIANA_RESULT=1"
type "%DIANA_RESULTS%\cierre_android.txt"
echo.
if "%DIANA_RESULT%"=="0" (echo Pruebas y compilaciones completadas. Falta registrar el recorrido en dispositivos.) else (echo Hay comprobaciones pendientes. Los detalles quedaron guardados para revisarlos.)
popd
pause
exit /b %DIANA_RESULT%
