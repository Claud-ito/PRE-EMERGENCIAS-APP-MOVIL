# AL-00: compilación y ejecución iOS desde macOS

**Responsable:** Alejandro Rojas Espinoza. **Base verificada:** SA-00 (`12cf0c5`) más la corrección de arranque de esta entrega. **Fecha:** 20/11/2026.

## Resultado

| Comprobación | Plataforma | Resultado |
| --- | --- | --- |
| `dotnet build` de la API | macOS 27.2 (arm64), SDK .NET 10.0.404 | Correcto, 0 advertencias. |
| `dotnet test` (reloj y auditoría) | macOS | 4/4 aprobadas. |
| Migración `Sa00AuditInitial` sobre SQL Server 2022 | Contenedor local (OrbStack) | Aplicada; `GET /api/v1/system/health` → `ready`. |
| `dotnet build -f net10.0-ios` | Simulador iOS arm64 | Correcto, 0 advertencias. |
| Arranque de la app en iOS | iPhone 18 Pro, iOS 27.2 (simulador) | **Fallaba** con `XamlParseException: StaticResource not found for key Headline`. Corregido (ver abajo). |
| Consulta de hora a la API desde la app | iPhone 18 Pro, iOS 27.2, `http://localhost:5080` | «API disponible» y hora UTC del servidor. Evidencia: `docs/evidencias/AL00_ios_simulador_api_disponible.png`. |

## Defecto detectado y corrección

`App` recibía `AppShell` por inyección en su constructor. El contenedor construía `AppShell` → `MainPage` **antes** de que `App.InitializeComponent()` cargara `Colors.xaml` y `Styles.xaml`; `MainPage` buscaba el estilo `Headline` con el diccionario vacío y la app se cerraba al iniciar. El fallo no depende de iOS: con la generación de XAML `SourceGen` también debería afectar a Android.

Corrección en `PRE-EMERGENCIAS-APP-MOVIL/App.xaml.cs`: `App` recibe `IServiceProvider` y resuelve `AppShell` en `CreateWindow`, después de cargar los recursos. Las páginas y servicios siguen registrándose en `MauiProgram`. **Said:** es un archivo de navegación compartida; revisar antes de integrar.

## Requisitos en macOS

1. **Xcode 27.2** desde la App Store (la versión debe coincidir con el SDK `Microsoft.iOS.Sdk.net10.0_27.2` del workload). Tras instalarlo:
   - aceptar la licencia al abrir Xcode;
   - `sudo xcode-select -s /Applications/Xcode.app/Contents/Developer` (con solo Command Line Tools, `xcodebuild` y `simctl` no funcionan);
   - descargar la plataforma **iOS 27.2 Simulator** desde Xcode.
2. **SDK .NET 10** y workloads. Si el SDK está en `~/.dotnet`, añadirlo al `PATH` (`export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"` y `export DOTNET_ROOT="$HOME/.dotnet"`).
3. `dotnet workload restore PRE-EMERGENCIAS-APP-MOVIL/PRE-EMERGENCIAS-APP-MOVIL.csproj`. En macOS el proyecto también apunta a `net10.0-maccatalyst`; sin el workload `maccatalyst` la restauración falla aunque solo se compile iOS.

## SQL Server mientras no exista una base compartida

El reparto prevé una base de pruebas compartida. Hasta que exista, en el Mac se usa un contenedor local (Docker u OrbStack; en Apple Silicon corre emulado con `--platform linux/amd64`):

```sh
docker run -d --name univalle-sql --platform linux/amd64 -e ACCEPT_EULA=Y \
  -e "MSSQL_SA_PASSWORD=<clave-de-desarrollo>" -p 1433:1433 \
  -v univalle-sql-data:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest
```

La conexión se guarda en `.env.mac.local` (ignorado por `.gitignore` mediante `.env.*`), con este contenido:

```sh
export ConnectionStrings__DefaultConnection='Server=tcp:localhost,1433;Database=UnivalleEmergenciasDev;User Id=sa;Password=<clave-de-desarrollo>;Encrypt=True;TrustServerCertificate=True'
```

Luego, una sola vez: `source .env.mac.local && dotnet ef database update --project src/Univalle.Emergencias.Api --startup-project src/Univalle.Emergencias.Api`. Cuando el equipo publique la base compartida, basta con cambiar la cadena de `.env.mac.local`.

## Ejecución diaria

```sh
./scripts/mac/run-api.sh                      # inicia el contenedor SQL si está detenido y la API en http://localhost:5080
dotnet build PRE-EMERGENCIAS-APP-MOVIL/PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64
xcrun simctl install booted PRE-EMERGENCIAS-APP-MOVIL/bin/Debug/net10.0-ios/iossimulator-arm64/PRE-EMERGENCIAS-APP-MOVIL.app
xcrun simctl launch --console-pty booted com.companyname.preemergenciasappmovil   # muestra excepciones de arranque
```

En la app, la URL del simulador iOS es `http://localhost:5080`; `NSAllowsLocalNetworking` ya permite HTTP local. Un iPhone físico necesita una dirección HTTPS alcanzable y un certificado confiable.

## Pendientes

- Probar la app en un iPhone físico (requiere firma con un Apple ID y la API publicada por HTTPS).
- Confirmar con el equipo la matriz de versiones mínimas (RNF06). El proyecto declara iOS 15.0 como mínimo; solo se probó iOS 27.2.
- `ApplicationId` sigue siendo el de la plantilla (`com.companyname.preemergenciasappmovil`); conviene acordar uno propio antes de firmar para dispositivo.
