# PRE-EMERGENCIAS-APP-MOVIL

La solución incluye la app .NET MAUI, una API ASP.NET Core, contratos compartidos y persistencia SQL Server. SA-01 a SA-03 incorporan acceso con cuentas reales, gestión de usuarios y buzón de sugerencias para incidentes cerrados. Los módulos de ubicaciones, brigadas, fichas e incidentes se integran en esta base.

## Requisitos

- SDK .NET 10 y cargas de trabajo .NET MAUI de las plataformas que se vayan a compilar.
- SQL Server accesible para la API. En Windows se puede comenzar con SQL Server Express LocalDB instalado; macOS necesita un SQL Server alcanzable por red.
- Android SDK/JDK para Android y Xcode en macOS para iOS.

Desde la raíz del repositorio:

```sh
dotnet workload restore PRE-EMERGENCIAS-APP-MOVIL/PRE-EMERGENCIAS-APP-MOVIL.csproj
dotnet tool restore
dotnet restore PRE-EMERGENCIAS-APP-MOVIL.slnx
```

Si solo se trabaja en la API, se puede restaurar y compilar `src/Univalle.Emergencias.Api/Univalle.Emergencias.Api.csproj` sin instalar MAUI.

## Arranque inmediato en Windows con LocalDB

`appsettings.Development.json` contiene una conexión de desarrollo a `(localdb)\MSSQLLocalDB` y a la base `UnivalleEmergenciasSa00Dev`. LocalDB es local a esa computadora; no sirve como base compartida para los otros integrantes.

```powershell
dotnet tool restore
dotnet ef database update --project src/Univalle.Emergencias.Api --startup-project src/Univalle.Emergencias.Api
dotnet run --project src/Univalle.Emergencias.Api --launch-profile http
```

En otra terminal:

```powershell
curl.exe http://localhost:5080/api/v1/system/health
curl.exe http://localhost:5080/api/v1/system/time
```

`health` responde `ready` solo si SQL Server está accesible y la tabla de auditoría de la migración existe. Si no, devuelve HTTP 503 con ProblemDetails y `code: database_unavailable`. `time` devuelve la hora UTC del servidor y no necesita la base de datos. La API no aplica migraciones automáticamente al arrancar: el comando anterior es un paso explícito.

## Conexión SQL Server para los cuatro equipos

Aún no hay un servidor SQL compartido asignado. Para integrar desde Windows y macOS, el equipo debe habilitar una instancia SQL Server con TCP, un host y puerto accesibles desde las cuatro computadoras y credenciales de desarrollo. Cada integrante ejecuta su API local apuntando a esa misma instancia. Se recomienda que una sola persona aplique la migración antes de que todos arranquen la API.

La variable `ConnectionStrings__DefaultConnection` reemplaza la conexión LocalDB sin guardar claves en Git. Ejemplo en PowerShell:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=tcp:HOST,1433;Database=UnivalleEmergenciasDev;User Id=USUARIO;Password=CLAVE;Encrypt=True;TrustServerCertificate=True"
dotnet ef database update --project src/Univalle.Emergencias.Api --startup-project src/Univalle.Emergencias.Api
dotnet run --project src/Univalle.Emergencias.Api --launch-profile http
```

Ejemplo en macOS con zsh:

```sh
export ConnectionStrings__DefaultConnection='Server=tcp:HOST,1433;Database=UnivalleEmergenciasDev;User Id=USUARIO;Password=CLAVE;Encrypt=True;TrustServerCertificate=True'
dotnet tool restore
dotnet ef database update --project src/Univalle.Emergencias.Api --startup-project src/Univalle.Emergencias.Api
dotnet run --project src/Univalle.Emergencias.Api --launch-profile http
```

`HOST`, `USUARIO` y `CLAVE` son valores propios del entorno; no son credenciales incluidas en esta entrega. `TrustServerCertificate=True` facilita un certificado SQL de desarrollo; para un entorno real se debe validar el certificado y administrar los secretos fuera del repositorio. Si la base ya fue migrada, los demás ejecutan solo `dotnet run`. La conexión se realiza desde la API; la app MAUI nunca se conecta directamente a SQL Server.

## App MAUI y URL de la API

La pantalla inicial guarda la URL base en las preferencias del dispositivo y consulta `GET /api/v1/system/time`. No se ingresan rutas, usuario ni contraseña en esa URL.

| Plataforma de desarrollo | URL inicial |
| --- | --- |
| App Windows con API en el mismo equipo | `http://localhost:5080` |
| Emulador Android con API en el equipo anfitrión | `http://10.0.2.2:5080` |
| Simulador iOS con API en el mismo Mac | `http://localhost:5080` |
| Dispositivo físico | Dirección HTTPS del equipo/servidor accesible desde el dispositivo y certificado confiable |

La excepción HTTP de Android se limita a `10.0.2.2`. iOS permite conexiones de red local para el simulador; los servicios que se publiquen deben usar HTTPS. La dirección de la API y la conexión SQL son configuraciones distintas. La verificación iOS desde macOS corresponde a AL-00; no fue ejecutada en este equipo Windows.

Para compilar la app en Windows:

```powershell
dotnet build PRE-EMERGENCIAS-APP-MOVIL/PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-windows10.0.19041.0
dotnet build PRE-EMERGENCIAS-APP-MOVIL/PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-android
```

En macOS, Alejandro puede comprobar iOS con `dotnet build PRE-EMERGENCIAS-APP-MOVIL/PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-ios` y abrir el proyecto en el entorno MAUI de su Mac. El nombre de la plataforma y las cargas de trabajo pueden requerir la versión compatible de Xcode instalada allí.

## Pruebas y estructura

```sh
dotnet test tests/Univalle.Emergencias.Tests/Univalle.Emergencias.Tests.csproj
```

Las pruebas verifican el reloj sustituible sin esperar seis horas y la persistencia de auditoría con SQLite en memoria. La migración de producción usa SQL Server y se comprobó con LocalDB en Windows.

| Ruta | Uso |
| --- | --- |
| `PRE-EMERGENCIAS-APP-MOVIL/` | App MAUI, navegación Shell y consulta inicial de la API. |
| `src/Univalle.Emergencias.Api/` | API, EF Core SQL Server, reloj y auditoría. |
| `src/Univalle.Emergencias.Contracts/` | Convenciones e interfaces compartidas. |
| `tests/Univalle.Emergencias.Tests/` | Pruebas de servicios comunes. |
| `docs/SA00_Integracion.md` | Reglas de integración y responsabilidades de los contratos. |
| `docs/SA01_SA03_Integracion.md` | Cuentas, permisos, buzón, datos de ensayo y verificación. |

Las consultas `/api/v1/system/time` y `/api/v1/system/health` son públicas para diagnóstico. Los módulos operativos usan la sesión y los permisos de la API; consulta la guía de integración para preparar la primera cuenta y los seis roles de ensayo.

Fuentes de las excepciones de red de desarrollo: [Android Network Security Configuration](https://developer.android.com/privacy-and-security/security-config) y [Apple NSAllowsLocalNetworking](https://developer.apple.com/documentation/bundleresources/information-property-list/nsapptransportsecurity/nsallowslocalnetworking).
