@echo off
setlocal
for %%I in ("%~dp0..\..") do set "DIANA_REPO=%%~fI"
pushd "%DIANA_REPO%"
if errorlevel 1 exit /b 1
set "UNIVALLE_TEST_SQLSERVER=Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True"
echo Verificando con SQL Server LocalDB. Las pruebas SQL crean bases temporales propias.
echo La salida y el informe se guardan en la carpeta Resultados_Diana_SQLServer junto al repositorio.
if not exist "..\Resultados_Diana_SQLServer" mkdir "..\Resultados_Diana_SQLServer"
dotnet test tests\Univalle.Emergencias.Tests\Univalle.Emergencias.Tests.csproj --no-restore -m:1 --logger "trx;LogFileName=Diana_SQLServer.trx" --results-directory "..\Resultados_Diana_SQLServer" > "..\Resultados_Diana_SQLServer\salida.txt" 2>&1
set "DIANA_RESULT=%ERRORLEVEL%"
type "..\Resultados_Diana_SQLServer\salida.txt"
echo.
if "%DIANA_RESULT%"=="0" (echo Pruebas completadas correctamente.) else (echo Hubo un problema. Conserva esta ventana para revisarlo.)
popd
pause
exit /b %DIANA_RESULT%
