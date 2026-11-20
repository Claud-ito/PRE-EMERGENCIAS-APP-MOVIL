# SA-00: acuerdos para integrar módulos

Esta base contiene la API, la app MAUI, contratos y pruebas. Las interfaces de Diana, Alejandro y Jorge fijan el intercambio mínimo; sus implementaciones pertenecen a las tareas de cada dueño.

## Convenciones comunes

| Tema | Acuerdo |
| --- | --- |
| Identificadores | `Guid`; `Guid.Empty` no es un ID válido. |
| Instantes | `DateTimeOffset` en UTC. La API lee `IServerClock`; la hora de Bolivia se usa para presentación. |
| HTTP | Prefijo `api/v1` y rutas comunes en `ApiRoutes`. |
| Errores | ProblemDetails con extensión `code`. Los códigos están en `ApiErrorCodes`. |
| App | Rutas Shell en `MobileRoutes`; `home` es la ruta inicial. Páginas y clientes se registran en `MauiProgram`. |
| API | Servicios registrados en `Program.cs`; `AppDbContext` tiene ámbito `scoped`. |
| Persistencia | EF Core 10 con SQL Server; migraciones explícitas. |

Los seis roles de `UserRole` son Comunidad/Testigo, SySO, Mantenimiento, Líder de brigada, Brigadista y Médico. «Encargado» agrupa a SySO y Mantenimiento en el UML; no es otra cuenta. `PermissionCodes` define los nombres de permisos. SA-01 resolverá su asignación, autenticará y comprobará en la API el estado activo de la cuenta en cada operación protegida.

## Contratos y responsables

| Contrato | Dueño | Uso acordado |
| --- | --- | --- |
| `IIdentityContext` | Said, SA-01 | Usuario autenticado, estado activo, rol y permisos; `null` sin sesión. |
| `ILocationCatalog` | Diana, DI-01 | Módulos y pisos activos, pertenencia e historial por ID. |
| `IBrigadeLeaderLookup` | Diana, DI-02 | Líder vigente por módulo, integrantes y especialidad. |
| `ITurnResolver` | Jorge, JO-01 | Encargado en un instante UTC con excepciones; `null` sin cobertura. |
| `IIncidentReader` | Alejandro, AL-01 | Incidente, ubicación, estado, coordinador y `ClosedAtUtc` opcional. |
| `INotificationSender` | Jorge, JO-02 | Registro y envío con `EventId` idempotente y `DeliveryStatus`. |
| `IServerClock` y `IAuditTrail` | Said, SA-00 | Hora sustituible y auditoría persistente. |

Los contratos están en `src/Univalle.Emergencias.Contracts`, sin dependencia de MAUI ni EF Core. Cada dueño puede ampliarlos coordinando cambios incompatibles con sus consumidores. El registro de DI de interfaces ajenas se añade cuando exista una implementación real.

## Reloj y auditoría

`IServerClock.UtcNow` usa `TimeProvider.System` por defecto. Una prueba puede inyectar otro `TimeProvider` y adelantarlo sin esperar seis horas. `GET /api/v1/system/time` devuelve la hora usada por la API; no la modifica.

`IAuditTrail.RecordAsync` recibe actor, operación, entidad y referencias opcionales a entidad e incidente. El servicio asigna ID y hora del servidor y persiste en `AuditEntries`. Para actor `User` se exige `ActorUserId`; para `System` se omite. No incluir claves, fichas clínicas u otros datos sensibles en `MetadataJson`.

`RecordAsync` llama a `SaveChangesAsync` sobre el `AppDbContext` del ámbito y guarda también cambios pendientes de ese contexto. Si una operación y su auditoría deben ser atómicas, el módulo debe compartir contexto y transacción explícita. Las nuevas entidades se incorporan mediante migraciones adicionales.

## Primer recorrido de integración

1. Restaurar la solución según el README.
2. Iniciar SQL Server, aplicar la migración y ejecutar la API.
3. Abrir `/api/v1/system/health` y confirmar `ready`.
4. Abrir la app, configurar la URL alcanzable desde el dispositivo y comprobar la hora.
5. Cada dueño implementa y registra su interfaz. Usar sustituciones locales solo en desarrollo y pruebas mientras falten datos reales.
6. Verificar API, app y pruebas antes de integrar. Alejandro comprueba iOS desde macOS (AL-00).

SA-00 no crea cuentas, reportes, catálogos, turnos, avisos ni incidentes. Todavía no existe un SQL Server compartido; la conexión configurable y los pasos para los cuatro equipos están preparados en el README.
