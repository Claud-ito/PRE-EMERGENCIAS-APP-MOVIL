# Verificación SA-01 a SA-03

**Versión verificada:** rama `feature/sa-identidad-buzon`, cambios de código hasta `734392bcda` (20/11/2026, hora de Bolivia). **Responsable:** Said Montaño Condori. **Entorno:** Windows, .NET 10, SQL Server LocalDB aislado `UnivalleEmergenciasSaidIntegrationDev`; app compilada para Windows y Android. La base de ensayo no forma parte del repositorio.

| Escenario | Plataforma y comprobación | Resultado |
| --- | --- | --- |
| Contraseñas con sal, sesiones vencidas y desactivación | Pruebas automatizadas con reloj controlado y SQLite | Aprobado: hashes distintos, credencial incorrecta rechazada, sesión vencida o cuenta inactiva sin acceso. |
| Alta, consulta, duplicados y baja de cuenta | Pruebas automatizadas y llamadas HTTP a la API con LocalDB | Aprobado: cuenta sin permiso recibió 403; tras la baja, la sesión anterior recibió 401. |
| Buzón accesible para alguien ajeno al caso | API HTTP con `testigo.prueba` y un incidente cerrado de ensayo | Aprobado: listado visible, ventana abierta y sugerencia registrada con la cuenta autenticada. |
| Límite exacto de seis horas y lectura posterior | Pruebas automatizadas con reloj controlado | Aprobado: escritura permitida antes del límite y rechazada justo en él y después; las sugerencias siguen legibles. |
| Migraciones y persistencia | LocalDB aislado; `dotnet ef migrations has-pending-model-changes` | Aprobado: migraciones aplicadas y sin cambios de modelo pendientes. |
| Suite completa, incluidas pruebas concurrentes de SQL Server | `dotnet test` con `UNIVALLE_TEST_SQLSERVER` apuntando a LocalDB | 33 aprobadas, 0 fallidas, 0 omitidas. |
| Compilación MAUI | `dotnet build` para Windows y Android | Windows: 0 errores, 0 advertencias. Android: 0 errores, 1 advertencia XA0033 por formato de versión de Java del equipo. |
| Alta de catálogo integrado con sesión real | API HTTP, cuenta SySO de ensayo, módulo y piso en LocalDB | Aprobado después de corregir las anotaciones de validación de los contratos de Diana. |

**Pendientes de integración:** recorrido funcional de MAUI en Android/iOS y compilación iOS en macOS; recepción real de avisos, coordinación por turno, AUTO y relevo. El remoto consultado el 20/11/2026 todavía contiene los contratos, pero no las implementaciones JO-01 a JO-04 de Jorge. Por eso no se declara aprobado el flujo SA-04 completo ni las medidas de entrega de avisos. Al llegar esos módulos, repetir pruebas de extremo a extremo y adjuntar evidencias de dispositivo y tiempos según el reparto.

**Actualización 22/11/2026:** Jorge integró JO-01 a JO-04 después de esta verificación histórica. La revisión posterior está en `SA04_Verificacion.md`.
