# Diana: integración hasta Sprint 2

Base de esta revisión: `main` en `03d9000`, con los incidentes de Alejandro, el PR #2 de Diana y SA-01/SA-02 de Said. El alcance sigue el reparto `V1_Reparto_Trabajo_UNIVALLE_Hasta_Sprint_2.md`.

## Servicios integrados

| Contrato | Implementación utilizada |
| --- | --- |
| `IIdentityContext` | `DatabaseIdentityContext` de Said: sesión Bearer persistida y cuenta activa, rol y permisos comprobados en cada solicitud. |
| `IBrigadeUserDirectory` | `BrigadeUserDirectory` de Said: cuentas activas de líder y brigadista de SA-02. |
| `IDianaMobileSession` | `DianaMobileSession` sobre la sesión MAUI compartida `MobileSession`; se registra antes de `AddDianaMobile()`. |
| `ILocationCatalog` | Catálogo real de Diana, registrado antes de las sustituciones de desarrollo de Alejandro. |
| `IIncidentReader` | `EfIncidentReader` de Alejandro sobre la tabla persistida `Incidents`. |
| `IBrigadeLeaderLookup` | Consulta real de Diana disponible para Jorge: módulo e instante UTC, líder vigente e integrantes/especialidades activos. |

La app utiliza la credencial de la sesión compartida y descarta su sesión local ante un 401. Los permisos se comprueban de nuevo en la API; ocultar menús no sustituye la autorización. SySO y Mantenimiento gestionan catálogos sin depender del turno. Solo Médico con `injuries.record` accede a las fichas; el rol de encargado no concede acceso clínico.

Una brigada puede abarcar varios módulos; cada módulo tiene como máximo una asignación vigente, protegida mediante índice único filtrado. Los cambios conservan el historial de asignaciones. La consulta no devuelve un líder desactivado ni una ubicación inactiva. No modifica al coordinador ni envía avisos: esas operaciones corresponden a Jorge.

RF05 usa los nombres confirmados **Primeros auxilios**, **Evacuación** y **Extinción de fuegos**, configurados en `Diana:BrigadeSpecialties` de la API.

## Migraciones y rutas

La migración de Diana `20261120192955_DianaCatalogsAndInjuriesAfterAl01` sigue a la de incidentes de Alejandro. Las migraciones posteriores de Said agregan identidad y sugerencias al mismo historial; no se crea otra base de usuarios ni se duplica el incidente. Las fichas tienen FK hacia `Incidents` con borrado restringido.

| Ruta | Operaciones |
| --- | --- |
| `/api/v1/locations/modules` | GET, POST; PUT/DELETE por ID. |
| `/api/v1/locations/modules/{id}/floors` | GET, POST. |
| `/api/v1/locations/floors/{id}` | PUT, DELETE. |
| `/api/v1/support-contacts` | GET, POST; GET/PUT/DELETE por ID. |
| `/api/v1/brigades` | GET, POST; PUT/DELETE por ID; GET `users` y `specialties`. |
| `/api/v1/brigades/modules/{id}/leader` | GET de brigada, líder e integrantes actuales; visible desde la pantalla de módulos. |
| `/api/v1/injury-records/incidents` | GET de selección mínima del caso para Médico. |
| `/api/v1/incidents/{id}/injury-records` | GET, POST para Médico. |

DELETE desactiva sin borrar físicamente los catálogos. `includeInactive=true` permite consultar sus referencias históricas. Las escrituras registran actor, operación, fecha y referencia; la auditoría de fichas incluye el incidente sin contenido clínico.

## Evidencia y límites

Ampliación del 22/11/2026 autorizada por el jefe a través de Diana: ficha e informe provisionales con datos ficticios. Consultar `DIANA_Plantilla_Clinica_Prueba.md`. El POST admite `testClinicalDraft` de forma opcional y hay un GET protegido `.../{recordId}/test-report`. Solo Médico accede a ambos. La migración `20261122191445_DianaTestClinicalDraft` agrega una columna nullable y conserva las fichas base. El prototipo está habilitado por configuración en Development y rechaza datos no marcados como prueba; no se habilita automáticamente en producción. Las comprobaciones móviles y conjuntas quedan para otro día por instrucción del jefe.

El 22/11/2026 se revisaron los resultados de la terminal de Diana: **38 pruebas aprobadas con SQL Server LocalDB, 0 fallidas y 0 omitidas**; MAUI Windows y Android compilaron con **0 errores y 0 advertencias**. `DianaIdentityIntegrationTests` comprueba por HTTP las sesiones reales de Said junto a catálogos, brigadas y fichas, incluida una variante sobre SQL Server migrado.

Los comandos para repetir las comprobaciones y la matriz de entrega están en `docs/DIANA_Entrega_Hoy.md`. No es necesario repetir la suite para un cambio exclusivamente documental.

Continúan pendientes el recorrido funcional Android/iOS y la validación externa de SySO sobre campos, informe, visibilidad y contingencia clínica. La plantilla ficticia incorporada después se describe en `DIANA_Plantilla_Clinica_Prueba.md`; no sustituye la aprobación clínica definitiva. No se agregan llamadas, derivaciones, convocatoria de especialidades, cierre productivo ni otras funciones del Sprint 3. Jorge entregó turno, avisos, AUTO y relevo en una integración posterior; la prueba de SA-04 de líder por módulo con AUTO se registra en `evidencias/SA04_Verificacion.md`.
