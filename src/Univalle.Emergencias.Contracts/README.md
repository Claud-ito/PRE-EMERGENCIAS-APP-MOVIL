# Contratos compartidos SA-00

Proyecto .NET 10 sin dependencias de MAUI ni EF Core. Los ID comunes son `Guid` y los instantes se expresan como `DateTimeOffset` UTC. En la app, la hora de Bolivia se usa solo para presentación. Las rutas HTTP se agrupan bajo `api/v1`; los errores de API usan ProblemDetails con un código estable en la extensión `code`.

| Contrato | Responsable de la implementación | Comportamiento mínimo |
| --- | --- | --- |
| `IIdentityContext` | Said | Usuario autenticado, estado activo, rol y permisos actualizados en el servidor. |
| `ILocationCatalog` | Diana | Módulos/pisos activos, relación válida e historial por ID. |
| `IBrigadeLeaderLookup` | Diana | Líder vigente por módulo e integrantes/especialidades. |
| `ITurnResolver` | Jorge | Encargado por instante o ausencia de cobertura (`null`). |
| `IIncidentReader` | Alejandro | Incidente, ubicación, estado, coordinador y cierre opcional. |
| `INotificationSender` | Jorge | Registro/envío con ID de evento idempotente y resultado técnico. |
| `IServerClock`, `IAuditTrail` | Said | Hora UTC inyectable y auditoría persistente con actor, operación y referencia. |

`UserRole` enumera los seis roles operativos. «Encargado» no es un séptimo rol. `PermissionCodes` define nombres de permisos, pero SA-00 no asigna todavía una matriz de permisos ni autentica usuarios: eso corresponde a SA-01. Los contratos de los otros integrantes son puntos de integración, no implementaciones simuladas registradas para producción.

Cada dueño puede extender sus records al implementar su módulo; debe coordinar cualquier cambio incompatible con quienes consumen el contrato. Los datos sensibles no deben incluirse en `AuditWriteRequest.MetadataJson`.
