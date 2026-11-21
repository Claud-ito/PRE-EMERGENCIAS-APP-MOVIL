# Integración SA-01 a SA-03: acceso, cuentas y buzón

## Preparar la base y el primer acceso

Aplica las migraciones antes de iniciar la API. En Windows, la conexión de `appsettings.Development.json` usa LocalDB; para una instancia compartida define `ConnectionStrings__DefaultConnection` según el README. No se guardan contraseñas en el repositorio.

```powershell
dotnet tool restore
dotnet ef database update --project src/Univalle.Emergencias.Api --startup-project src/Univalle.Emergencias.Api
$env:Bootstrap__Username = 'siso.inicial'
$env:Bootstrap__Email = 'siso@example.test'
$env:Bootstrap__FullName = 'SySO inicial'
$env:Bootstrap__Password = '<contraseña propia de 12 a 128 caracteres>'
dotnet run --project src/Univalle.Emergencias.Api --launch-profile http -- --bootstrap-siso
dotnet run --project src/Univalle.Emergencias.Api --launch-profile http
```

`--bootstrap-siso` solo funciona en una base sin cuentas. Después, SySO o Mantenimiento crean las demás desde la pestaña **Usuarios** o `POST /api/v1/users`. Las cuentas se desactivan lógicamente: la API revoca sus sesiones y conserva las referencias históricas. Cambiar rol o contraseña también revoca sus sesiones anteriores. Una sesión dura ocho horas y se verifica con el estado actual de la cuenta en cada solicitud.

Para crear **seis cuentas de ensayo** en una base de desarrollo, define una contraseña temporal fuera de Git y ejecuta el comando explícito. No se ejecuta al arrancar normalmente ni en producción.

```powershell
$env:DevelopmentAccounts__Password = '<contraseña temporal de 12 a 128 caracteres>'
dotnet run --project src/Univalle.Emergencias.Api --launch-profile http -- --seed-development-users
```

Los usuarios son `testigo.prueba`, `siso.prueba`, `mantenimiento.prueba`, `lider.prueba`, `brigadista.prueba` y `medico.prueba`. El comando omite los nombres que ya existan y no cambia contraseñas de cuentas existentes.

## Sesión y permisos

`POST /api/v1/auth/login` recibe `username` y `password` y devuelve `accessToken`, vencimiento y permisos. `GET /api/v1/auth/me` comprueba la sesión y `POST /api/v1/auth/logout` la revoca. Las rutas protegidas esperan `Authorization: Bearer <token>`; no aceptan un rol, usuario o fecha enviado por el formulario como identidad. La app guarda el token en el almacenamiento protegido del dispositivo y lo descarta si cambia la dirección de la API.

| Rol | Operaciones principales |
| --- | --- |
| Comunidad/Testigo | Reportar y usar el buzón. |
| SySO y Mantenimiento | Gestionar cuentas, ubicaciones, brigadas y contactos; coordinar casos asignados. El acceso administrativo no depende del turno. |
| Líder de brigada y Brigadista | Reportar, usar el buzón y recibir avisos cuando el módulo esté integrado. |
| Médico | Reportar, usar el buzón y registrar/consultar fichas médicas. |

Todos los roles activos pueden leer y escribir sugerencias, incluso si no participaron en el incidente. La escritura depende además de la ventana de seis horas. El menú MAUI se ajusta a los permisos recibidos, pero la API vuelve a comprobarlos en cada operación. Una cuenta sin permiso recibe 403 y una sesión ausente, vencida o revocada recibe 401.

## Buzón de sugerencias RF11

`GET /api/v1/suggestions/incidents` lista incidentes cerrados y su módulo/piso. `GET /api/v1/suggestions/incidents/{id}` entrega las sugerencias y el plazo, aun después de su vencimiento. `POST` en la misma ruta registra texto de 1 a 1000 caracteres. La API obtiene autor y hora de la sesión y del reloj del servidor. La escritura se permite desde `ClosedAtUtc` inclusive hasta `ClosedAtUtc + 6 h` exclusivo. Al vencimiento responde 409 con `code: suggestion_window_closed`; la lectura continúa. El buzón solo entrega autor, texto y fecha: no expone fichas médicas ni los aportes del reporte.

El cierre formal productivo pertenece a RF19 de Sprint 3. Para comprobar RF11 en Sprint 2, en una base de desarrollo con las cuentas de ensayo y un módulo/piso activos, ejecuta:

```powershell
dotnet run --project src/Univalle.Emergencias.Api --launch-profile http -- --seed-closed-incident
```

El comando crea un incidente cerrado treinta minutos antes de ejecutarlo y muestra su ID. Es idempotente para ese ID; usa otra base de ensayo si necesitas comenzar otra ventana. No publica un botón ni un endpoint de cierre simulado. Las pruebas automatizadas avanzan el reloj antes, justo en y después del límite de seis horas sin esperar tiempo real.

## Verificación y límites de integración

```powershell
$env:UNIVALLE_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True'
dotnet test tests/Univalle.Emergencias.Tests/Univalle.Emergencias.Tests.csproj
dotnet build PRE-EMERGENCIAS-APP-MOVIL/PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-windows10.0.19041.0
dotnet build PRE-EMERGENCIAS-APP-MOVIL/PRE-EMERGENCIAS-APP-MOVIL.csproj -f net10.0-android
```

Las pruebas SQL crean y eliminan bases temporales; no apuntes `UNIVALLE_TEST_SQLSERVER` a datos que necesites conservar. La integración final SA-04 requiere los servicios reales de turno, avisos, AUTO y relevo de Jorge (JO-01 a JO-04). Hasta que lleguen, los incidentes de Alejandro usan sustituciones exclusivas de Development para turno y envío de avisos; la aceptación del flujo completo y las mediciones de recepción quedan pendientes. La compilación y recorrido iOS del incremento corresponden al equipo con macOS; esta máquina Windows no puede verificarlos.
