# AL-01 / AL-02 — Registro de incidente, unión concurrente y alerta inicial

Responsable: Alejandro Rojas Espinoza · Rama: `feature/al-incidentes`
Requisitos: RF07 (registro del reporte), RF08 (unión por módulo + piso), RNF05 (≤ 3 pasos principales).

## 1. Qué se entrega

| Pieza | Ubicación |
|---|---|
| Contrato público del módulo | `src/Univalle.Emergencias.Contracts/Incidents/IncidentContracts.cs` |
| Entidades y configuración EF (propias) | `src/Univalle.Emergencias.Api/Incidents/IncidentEntities.cs`, `IncidentModelConfiguration.cs` |
| Migración | `Data/Migrations/20261120140743_Al01IncidentsAndReports` |
| Servicio de reporte y unión | `Incidents/IncidentReportService.cs` |
| Alerta inicial (vía `INotificationSender`) + reintentos | `Incidents/InitialAlertDispatcher.cs` |
| Lectura (`IIncidentReader`) y consultas | `Incidents/IncidentQueries.cs` |
| Endpoints | `Incidents/IncidentsController.cs` |
| Sustitutos de desarrollo | `Incidents/Development/IncidentDevelopmentSubstitutes.cs` |
| Pantallas MAUI | `PRE-EMERGENCIAS-APP-MOVIL/Incidents/ReportIncidentPage`, `CoordinatedIncidentsPage` |
| Cliente HTTP móvil | `PRE-EMERGENCIAS-APP-MOVIL/Services/IncidentApiClient.cs` |
| Pruebas | `tests/Univalle.Emergencias.Tests/Incidents/` |

## 2. Endpoints (`api/v1/incidents`)

| Método y ruta | Permiso | Respuestas |
|---|---|---|
| `GET report-options` | `ReportIncident` | 200 módulos activos con sus pisos activos |
| `POST reports` | `ReportIncident` | 201 `SubmitIncidentReportResponse` · 400 errores por campo · 422 `location_unavailable` |
| `GET assigned` | `CoordinateAssignedIncident` | 200 incidentes activos del coordinador con todos sus aportes |
| `GET {incidentId}/reports` | `CoordinateAssignedIncident` + ser el coordinador actual | 200 · 403 `not_incident_coordinator` · 404 |

- El informante sale de la sesión (`IIdentityContext`) y la hora del reporte de `IServerClock`. La app no envía ninguno de los dos.
- `ClientRequestId` lo genera la app una vez por formulario. Un reenvío con el mismo valor devuelve el mismo resultado sin crear otro aporte: índice único (`ReporterUserId`, `ClientRequestId`).
- `IncidentCreated` indica si el reporte abrió el caso (`true`) o se agregó como aporte a uno activo (`false`).

## 3. Garantía de concurrencia (RF08)

1. El índice único filtrado `UX_Incidents_ActiveLocation` sobre (`ModuleId`, `FloorId`) con `WHERE [Status] = 'Active'` impide en la base de datos que haya dos incidentes activos en la misma ubicación.
2. Si dos solicitudes intentan abrir el caso a la vez, una falla con `DbUpdateException`. El servicio limpia el contexto y reintenta (máximo 3 veces); en el reintento encuentra el incidente ya confirmado y agrega su reporte como aporte.
3. Todos los aportes se conservan. Solo el que abrió el caso queda con `IsInitial = true`.
4. La alerta inicial tiene un `InitialAlertEventId` que se guarda con el incidente. Se envía después del commit mediante `INotificationSender` (JO-02); no hay otro emisor. Si el envío falla, `InitialAlertRetryWorker` reintenta con el **mismo** EventId (hasta 5 intentos, con espera creciente). El token `ConcurrencyStamp` evita que dos procesos reserven el mismo intento.

Evidencia (SQL Server 2022 real):
- 25 reportes simultáneos producen 1 incidente, 25 aportes, 1 alerta y 1 auditoría `incident.opened`.
- El índice único resolvió varias carreras.

### Cómo ejecutar las pruebas

```bash
dotnet test tests/Univalle.Emergencias.Tests
```

Sin SQL Server se ejecutan 15 pruebas con SQLite en memoria y se omiten 2. Para incluir las de concurrencia contra SQL Server, cada una crea y elimina su propia base temporal:

```bash
UNIVALLE_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True" dotnet test tests/Univalle.Emergencias.Tests
```

Resultado al 2026-11-20: 17/17 correctas.

## 4. App móvil

- **Reportar** (RNF05): un solo formulario con 3 pasos (1. tipo, 2. módulo y piso, 3. gravedad preliminar, con detalles opcionales) y el botón Enviar. Las ubicaciones vienen de `report-options`.
- Mensajes de la pantalla Reportar:
  - Los errores se muestran en cada campo.
  - Si la ubicación ya no está disponible (422), la lista se recarga.
  - Si falla la red, se conservan los datos y el mismo `ClientRequestId`, de modo que reenviar no duplica el aporte.
- **Incidentes** (observaciones del equipo, 2026-11-27): pestaña única del encargado (SySO/Mantenimiento); para él reemplaza a Avisos. Líder, brigadista y médico siguen usando Avisos.
  - Una tarjeta por incidente. La cabecera muestra tipo, módulo y piso, la hora de la alerta inicial, la gravedad y el total de aportes. Si la alerta inicial no está leída, aparece «Alerta nueva».
  - Un contador verde indica los aportes nuevos desde la última vez que se abrió la tarjeta (se guarda por usuario en el dispositivo). Al abrir la tarjeta el contador desaparece y la alerta inicial queda leída (`PATCH notifications/{id}/read`).
  - Al abrir la tarjeta se ven los aportes con el nombre del informante, por ejemplo «Testigo de prueba (testigo.prueba)», en vez de su ID. La API los entrega en `ReporterName`/`ReporterUsername`.
  - RF09 en la tarjeta: estado de la convocatoria (manual, AUTO o el plazo AUTO pendiente) y el botón «Registrar convocatoria ya realizada al líder», que queda deshabilitado una vez registrada. La API agrega `AutoDueAtUtc`, `CalloutAtUtc` y `CalloutChannel` a la vista. El relevo se propone dentro de la tarjeta; los relevos recibidos se aceptan arriba de la lista.
  - Se actualiza sola cada 2 s mientras está visible, y también deslizando hacia abajo. Ya no hay botón Actualizar.
  - Si llega una alerta estando en otra pestaña, aparece «Ver incidente», que abre esta pestaña, y su título muestra los avisos sin leer, por ejemplo «Incidentes (1)».
- Verificado en el simulador iPhone 18 Pro con iOS 27.2 contra la API local:
  - `docs/evidencias/AL01_ios_reporte_enviado.png`
  - `docs/evidencias/AL02_ios_coordinador_aportes.png`: incidente con 2 aportes, uno desde la app y otro de un segundo testigo.
  - `docs/evidencias/AL02_ios_testigo_sin_permiso.png`: un testigo recibe 403 en el seguimiento.

## 5. Sustitutos de desarrollo (temporales)

**Actualización SA-04:** la API y la app ya usan cuentas y tokens reales, ubicaciones de Diana y turnos/avisos persistentes de Jorge. Aunque el código de sustituciones sigue disponible para desarrollo, `TryAdd` no lo selecciona porque los servicios reales se registran primero. `X-Dev-User-Id`, `X-Dev-Role` y `DevSessionSettings` no son el modo de acceso de la app actual.

Solo se registran con `ASPNETCORE_ENVIRONMENT=Development` y `IncidentDevSubstitutes:Enabled=true` (valor por defecto). Usan `TryAdd`, así que cuando SA-01, SA-02 o JO-01/02 registren sus implementaciones reales, esas tienen prioridad.

| Sustituto | Reemplaza a | Comportamiento |
|---|---|---|
| `DevHeaderIdentityContext` | SA-01 | Cabeceras `X-Dev-User-Id` y `X-Dev-Role`. Todos los roles pueden reportar; SySO y Mantenimiento también coordinan. |
| `DevLocationCatalog` | SA-02 | Módulos de prueba A (planta baja, piso 1 y piso 2 inactivo) y B (planta baja). |
| `DevTurnResolver` | JO-01 | Encargado fijo tomado de `IncidentDevSubstitutes:CoordinatorUserId`; sin ese valor no hay cobertura. |
| `LoggingNotificationSender` | JO-02 | Solo escribe `[DEV] Aviso ...` en el log. |

La app envía el token de la sesión real al módulo de incidentes.

## 6. Pendientes y coordinación con el equipo

- **Said (SA-00/SA-01/SA-02):**
  1. `App.xaml.cs`: corrección de arranque (ver `AL00_iOS_macOS.md`).
  2. `AppDbContext`: se agregaron `DbSet`s y `ApplyConfigurationsFromAssembly`.
  3. `AppShell`: se agregaron las pestañas Reportar y Mis incidentes; la navegación final por rol la decide SA.
  4. `ILocationCatalog` real debe implementar `IsActiveFloorInModuleAsync`.
- **Jorge (JO-01/02/04):**
  1. `ITurnResolver` e `INotificationSender` reales: no se requiere ningún cambio en mi código.
  2. JO-04 (relevo) debe actualizar `Incident.CurrentCoordinatorUserId` renovando `ConcurrencyStamp`.
- **Contrato:** `IncidentSnapshot` incluye `OpenedAtUtc` e `InitialAlertEventId`; `ClosedAtUtc` es opcional. Se agregaron las constantes `IncidentStatuses` e `IncidentMobileRoutes`.
- **AL-03:** integración con el servicio de turnos real y AUTO; depende de JO-01/02/03.
- **AL-04:** verificación en iOS del incremento integrado; queda pendiente probar en un iPhone físico.
