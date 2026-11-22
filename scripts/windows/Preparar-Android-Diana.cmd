@echo off
setlocal
for %%I in ("%~dp0..\..") do set "DIANA_REPO=%%~fI"
set "DIANA_JDK=%~1"
set "DIANA_LICENSE_OPTION="
if /i "%~1"=="--aceptar-licencias" (
    set "DIANA_JDK=%~2"
    set "DIANA_LICENSE_OPTION=-p:AcceptAndroidSdkLicenses=true"
)
if not defined DIANA_JDK if exist "%JAVA_HOME%\bin\javac.exe" set "DIANA_JDK=%JAVA_HOME%"
if not defined DIANA_JDK for /d %%J in ("%DIANA_REPO%\..\toolchains\jdk21\jdk-*") do if exist "%%~fJ\bin\javac.exe" set "DIANA_JDK=%%~fJ"
if not defined DIANA_JDK (
    echo Indica la carpeta de un JDK 21 como primer argumento del script.
    pause
    exit /b 1
)
set "DIANA_ANDROID_SDK=%LOCALAPPDATA%\Android\Sdk"
if defined ANDROID_HOME set "DIANA_ANDROID_SDK=%ANDROID_HOME%"
pushd "%DIANA_REPO%"
if errorlevel 1 exit /b 1
echo Preparando los componentes Android requeridos por el proyecto con JDK 21.
echo Si se solicita aceptar una licencia, revisala antes de responder.
if defined DIANA_LICENSE_OPTION echo Aceptacion de licencias solicitada explicitamente mediante --aceptar-licencias.
dotnet build PRE-EMERGENCIAS-APP-MOVIL\PRE-EMERGENCIAS-APP-MOVIL.csproj -t:InstallAndroidDependencies -f net10.0-android -m:1 "-p:JavaSdkDirectory=%DIANA_JDK%" "-p:AndroidSdkDirectory=%DIANA_ANDROID_SDK%" %DIANA_LICENSE_OPTION%
if errorlevel 1 (
    echo La preparacion no termino. Conserva esta ventana para revisarlo.
    popd
    pause
    exit /b 1
)
set "DIANA_RESULTS=%DIANA_REPO%\..\Resultados_Diana_Integracion"
if not exist "%DIANA_RESULTS%" mkdir "%DIANA_RESULTS%"
echo Compilando Android. Windows y las 38 pruebas aprobadas no se repiten.
dotnet build PRE-EMERGENCIAS-APP-MOVIL\PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-android -m:1 "-p:JavaSdkDirectory=%DIANA_JDK%" "-p:AndroidSdkDirectory=%DIANA_ANDROID_SDK%" > "%DIANA_RESULTS%\cierre_android.txt" 2>&1
set "DIANA_RESULT=%ERRORLEVEL%"
type "%DIANA_RESULTS%\cierre_android.txt"
popd
pause
exit /b %DIANA_RESULT%
