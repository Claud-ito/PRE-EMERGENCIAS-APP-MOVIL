# SA-04: integración y estado de aceptación

**Revisión:** 22/11/2026 (hora de Bolivia). **Versión de código revisada:** `feature/sa04-integracion-auditoria`, desde `bb4272b3b2` hasta `0f83d8626e`. **Responsable de esta revisión:** Said Montaño Condori. **Entorno:** Windows, .NET 10 y bases temporales SQL Server LocalDB; SQLite en memoria para la prueba cruzada de brigada y AUTO.

## Resultado comprobado

| Escenario | Evidencia | Resultado |
| --- | --- | --- |
| Servicios reales de Jorge | Registro de `DatabaseTurnResolver`, `PersistentNotificationSender`, `CalloutService` y `AutoWorker` en la API; sustituciones con `TryAdd` al final | Los servicios reales tienen prioridad también en Development. |
| Diana → Said → Jorge | Prueba `Sa04BrigadeAutoIntegrationTests`: brigada de Diana, directorio de cuentas reales de SA-02, consulta real del líder y AUTO de Jorge | AUTO avisó al líder vigente del módulo y, tras cambiarlo, al nuevo líder. |
| Flujo HTTP con reinicio | `scripts/windows/Verificar-Jorge.ps1` sobre una base LocalDB temporal; cuentas reales, turno, incidente, lectura de alerta, reinicio de API y AUTO | Alerta inicial al encargado; un único aviso AUTO al líder a los 120,69 s de abrir el incidente. El encargado conservó el mando. |
| Auditoría de horario y excepciones | Pruebas de coordinación sobre `AuditEntries` | Alta, edición y baja de horarios y excepciones guardan actor, acción y hora del servidor. |
| Auditoría de AUTO y relevo | Pruebas de coordinación sobre `AuditEntries` | Preferencia AUTO, convocatoria manual/automática, propuesta y aceptación de relevo quedan registradas. La convocatoria incluye destinatario e incidente. |
| Suite completa | `dotnet test` con `UNIVALLE_TEST_SQLSERVER` en LocalDB | 63 aprobadas, 0 fallidas, 0 omitidas. |
| Persistencia | `dotnet ef migrations has-pending-model-changes` | No hay cambios de modelo pendientes de migración. |
| Compilación MAUI | `dotnet build` Windows y Android | Windows: 0 errores, 0 advertencias. Android: 0 errores; una advertencia XA0033 del formato de versión del JDK local. |

La evidencia anterior de Jorge de un recorrido HTTP y una alerta Android está en `JO00-JO05.md`. El recorrido HTTP de esta tabla se repitió en esta revisión; la captura Android sigue siendo evidencia de Jorge y no se atribuye a SA-04.

## Pendientes para cerrar la aceptación de los sprints

- **Diana / SySO:** la ficha e informe actuales usan una plantilla ficticia de ensayo; SySO aún debe aprobar campos, visibilidad y contingencia sin Médico. RF10 clínico no se declara definitivo.
- **Diana y Alejandro:** falta recorrer sus pantallas integradas con cuentas reales en Android e iOS y guardar evidencia de dispositivo. Compilar la app no sustituye ese recorrido.
- **Jorge y Alejandro:** falta medir y documentar la recepción de la alerta y AUTO en iOS; la captura Android disponible corresponde a la alerta inicial, no a AUTO. AL-04 y la comprobación móvil completa de JO-05 siguen abiertos.
- **RNF02:** la meta de disponibilidad del 95 % requiere una ventana y horario de observación acordados; no se demuestra con esta prueba breve.

No se realizó una prueba conjunta en dispositivos conectados a una misma API/base de ensayo. Al reunir esas evidencias y la decisión clínica de SySO, repetir la suite y actualizar esta matriz con versión, plataforma, resultado y responsable.
