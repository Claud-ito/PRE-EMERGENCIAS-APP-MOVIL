# Diana — integración hasta Sprint 2 sobre main ecd0883

Base remota revisada el 20/11/2026: `ecd0883f6ed49cbf41aa3d9ba94eed1e9144f33e`.
Incluye el [PR #1 de Alejandro](https://github.com/Claud-ito/PRE-EMERGENCIAS-APP-MOVIL/pull/1), integrado el 20/11/2026 a las 14:24 de Bolivia. Al revisar no había entregas nuevas de Said ni Jorge.
Se conserva el alcance asignado a Diana en `V1_Reparto_Trabajo_UNIVALLE_Hasta_Sprint_2.md`. Las especialidades quedaron confirmadas al recibir el contenido tabular de RF05: Primeros auxilios, Evacuación y Extinción de fuegos.

## Entregas y dependencias

| Tarea | Avance preparado | Pendiente |
| --- | --- | --- |
| DI-01 / RF04 | API y MAUI de módulos/pisos, pertenencia, bajas lógicas y lectura histórica. `ILocationCatalog` real se registra antes de las sustituciones de Alejandro. Su reporte consulta y valida estas ubicaciones. | Cuentas y permisos reales SA-01; comprobación móvil. |
| DI-02 / RF05 | API y MAUI de brigadas, integrantes, líder y varios módulos. Historial de asignaciones; índice único por módulo vigente. `IBrigadeLeaderLookup` preparado para AUTO. | Cuentas SA-02 mediante `IBrigadeUserDirectory`; conexión con Jorge. |
| DI-03 / RF06 | API y MAUI de consultorio, centros médicos y bomberos; altas, consulta, edición y desactivación conservando referencias. | Sesión real y comprobación móvil. |
| DI-04 / RF10 | Fichas relacionadas con la tabla real `Incidents`, FK con borrado restringido, `EfIncidentReader` de Alejandro y selector mínimo de incidentes para Médico. Creación después de evaluar/atender; cero o varias fichas por caso. | Sesión real del Médico y comprobación móvil. |
| DI-05 | Pruebas del flujo integrado y de permisos. | Plantilla clínica, visibilidad para encargados y contingencia ante ausencia del médico, pendientes de SySO. |

La ficha base solo almacena ID, incidente, médico y fecha del servidor. No identifica todavía al paciente ni sustituye la plantilla clínica pendiente. No declarar RF10 completamente validado. No se agregan llamadas, derivaciones, convocatoria de especialidades, cierre productivo ni otras funciones del Sprint 3.

## Integración con los compañeros

### Identidad y cuentas de Said

**Actualización SA-01/SA-02:** `IIdentityContext`, `IBrigadeUserDirectory` e `IDianaMobileSession` ya están conectados con usuarios reales. Las indicaciones siguientes describen los contratos originales; el acceso actual se explica en `SA01_SA03_Integracion.md`. El recorrido móvil e iOS todavía requiere verificación en dispositivo o simulador.

- Registrar la implementación real de `IIdentityContext`. El filtro de Diana comprueba cuenta activa, rol y permiso en cada solicitud. Sin servicio devuelve 503, sin sesión 401, sin autorización 403.
- SySO y Mantenimiento gestionan catálogos sin consultar el turno. Médico con `injuries.record` consulta/crea fichas y selecciona el caso; SySO y Mantenimiento no reciben acceso clínico por su rol.
- Implementar `IBrigadeUserDirectory` sobre las cuentas reales de SA-02 y registrarlo con ámbito `scoped`. No se mantienen cuentas paralelas. Líder e integrantes deben estar activos y tener rol compatible.
- En MAUI registrar la sesión real mediante `IDianaMobileSession`, agregando la credencial real en `AuthorizeAsync`. Registrar ese adaptador antes de `AddDianaMobile()` o reemplazar explícitamente el registro provisional. Mientras falte, las páginas permanecen cerradas.
- No se amplían los permisos de las cabeceras `X-Dev-*` de Alejandro ni se inventa un inicio de sesión. Sus sustituciones de desarrollo continúan limitadas a su propósito; la integración productiva exige SA-01.
- Las FK físicas hacia usuarios esperan el esquema real SA-02. La FK ficha-incidente ya está implementada.

### Incidentes de Alejandro

- Se conserva su contrato ampliado `IncidentSnapshot` con `OpenedAtUtc` e `InitialAlertEventId`, su registro de servicios, navegación y corrección del orden de carga de recursos MAUI.
- `AddDianaModules()` se ejecuta antes de `AddIncidentDevelopmentSubstitutes()`: el `TryAdd` de Alejandro conserva el catálogo real y no registra el simulado. Los servicios de ubicaciones e incidentes quedan comprobados conjuntamente.
- `InjuryRecordsController` consume su `IIncidentReader` real; no se duplica la tabla de incidentes. La base exige un incidente existente mediante FK y conserva las fichas al desactivar ubicaciones.
- `GET /api/v1/injury-records/incidents` entrega al Médico únicamente ID, módulo, piso, estado y apertura para seleccionar el caso atendido, sin aportes ni contenido clínico. Conserva también la identificación de ubicaciones históricas.
- La pantalla admite selección de caso y navegación directa con `Shell.Current.GoToAsync($"diana-injuries?incidentId={id}")`.

### Líder para Jorge

- Inyectar `IBrigadeLeaderLookup` con módulo e instante UTC. Una brigada puede abarcar varios módulos; cada módulo tiene como máximo una asignación vigente.
- El servicio devuelve ausencia de líder ante módulo inactivo o líder no activo. Si falta el adaptador real de cuentas, informa la dependencia; no utiliza usuarios ficticios como destinatarios.
- No modifica coordinadores, turnos ni envía avisos; esas operaciones corresponden a Jorge.

### Especialidades

RF05 confirma exactamente **Primeros auxilios**, **Evacuación** y **Extinción de fuegos**. Están incluidas en `Diana:BrigadeSpecialties` en `appsettings.json` y la pantalla las obtiene de `GET /api/v1/brigades/specialties`. Este pendiente queda resuelto. La asignación de personas sigue dependiendo de las cuentas reales SA-02. Las comprobaciones verifican los tres nombres exactos y su consulta por SySO y Mantenimiento, además del rechazo de una configuración ausente.

## Migraciones y rutas

La nueva migración `20261120192955_DianaCatalogsAndInjuriesAfterAl01` se generó sobre el modelo de Alejandro y va después de `20261120140743_Al01IncidentsAndReports`. Conserva auditoría, incidentes y reportes; añade solo las tablas de Diana y la FK ficha-incidente.
Reemplaza la migración del borrador anterior `20261120135701_DianaCatalogsAndInjuryBase`, que no se aplicó en esta sesión. No combinar ambas. Si esa versión antigua se aplicó en otra base, reconciliar el historial antes de actualizarla.

```sh
dotnet ef database update --project src/Univalle.Emergencias.Api --startup-project src/Univalle.Emergencias.Api
dotnet run --project tests/Univalle.Emergencias.Diana.Checks
dotnet test tests/Univalle.Emergencias.Tests/Univalle.Emergencias.Tests.csproj
```

| Ruta base | Operaciones |
| --- | --- |
| `/api/v1/locations/modules` | GET, POST; PUT/DELETE por ID. |
| `/api/v1/locations/modules/{id}/floors` | GET, POST. |
| `/api/v1/locations/floors/{id}` | PUT, DELETE. |
| `/api/v1/support-contacts` | GET, POST; GET/PUT/DELETE por ID. |
| `/api/v1/brigades` | GET, POST; PUT/DELETE por ID; GET `users` y `specialties`. |
| `/api/v1/brigades/modules/{id}/leader` | GET de brigada, líder e integrantes actuales; visible desde la pantalla de módulos. |
| `/api/v1/injury-records/incidents` | GET de selección mínima para Médico. |
| `/api/v1/incidents/{id}/injury-records` | GET, POST para Médico. |

DELETE desactiva; no borra físicamente. `includeInactive=true` muestra historial en los listados autorizados. Cada escritura registra actor, operación, fecha y referencia; las fichas incluyen incidente sin datos clínicos en auditoría.

## Evidencia del 20/11/2026

- API, contratos, pruebas y comprobaciones compilaron con las versiones reales 10.0.15, sin errores ni advertencias.
- Pasaron 64 comprobaciones de permisos, pipeline MVC en memoria, relaciones, índices, instantánea de migración y catálogo de especialidades. Incluyen la lectura de la configuración real de la API y los nombres exactos de RF05 para SySO y Mantenimiento.
- Se ejecutaron directamente 24 métodos Fact con sus aserciones xUnit y SQLite en memoria, preservando la omisión de tres casos que exigen SQL Server real. El testhost habitual está bloqueado en este entorno; no se presenta esta ejecución como una ejecución de `dotnet test`.
- Los escenarios incluyen reportes y reintentos de Alejandro, auditoría, catálogos, bajas, cero/varias fichas, rechazo de fichas huérfanas y prioridad del catálogo real sobre la sustitución de desarrollo.
- Después de las verificaciones anteriores, Diana ejecutó `scripts/windows/Verificar-Diana.cmd` en su terminal Windows. Se revisaron la salida y los contadores del informe TRX: **27 pruebas aprobadas, 0 errores, 0 omitidas** mediante `dotnet test`, incluyendo los tres escenarios de SQL Server LocalDB (concurrencia y migración/brigadas/historial).
- Falta probar el funcionamiento MAUI Android/iOS con la sesión real. La verificación iOS de Alejandro no valida automáticamente las nuevas pantallas de Diana.

Las pruebas adicionales cubren brigadas con varios módulos, consulta del líder por módulo, cambio de líder, conservación del historial y rechazo de integrantes o asignaciones inválidos. `scripts/windows/Verificar-Diana.cmd` permite repetir la ejecución habitual de las pruebas y los tres escenarios de SQL Server sobre LocalDB, ya aprobados en esta entrega. El detalle de entrega y comprobación está en `docs/DIANA_Entrega_Hoy.md`.

La rama `diana/sprint-2` se trabaja ahora en un clon con el historial remoto del equipo. Los commits adicionales requieren publicarse para que aparezcan en GitHub.
