# Entrega de Diana hasta Sprint 2

Fecha de verificación: 22/11/2026. Integración sobre `main` en `03d9000`, que incluye el PR #2 de Diana y la entrega de Said.

## Resultado

La sesión compartida de Said y su directorio de cuentas ya están conectados a los módulos de Diana. No se requiere otra implementación de identidad ni cuentas paralelas.

| Comprobación | Resultado verificado |
| --- | --- |
| Suite completa con SQL Server LocalDB | 38 ejecutadas, 38 aprobadas, 0 fallidas y 0 omitidas. |
| MAUI para Windows | Compilación correcta, 0 errores y 0 advertencias. |
| MAUI para Android | Compilación correcta, 0 errores y 0 advertencias, usando JDK 21 y SDK API 36. |
| Recorrido funcional en dispositivos Android/iOS | Pendiente; la compilación no sustituye la comprobación de interfaz. |

Se revisaron los informes generados en la terminal de Diana: `Cierre_Diana_Said.trx`, `cierre_sql.txt`, `cierre_windows.txt` y `cierre_android.txt`, guardados en `Resultados_Diana_Integracion` junto al repositorio. Son resultados de este clon; no se presentan las capturas anteriores de Alejandro o la evidencia de Said como pruebas nuevas de las pantallas de Diana.

## Alcance entregado

| Tarea | Implementación y comprobación |
| --- | --- |
| DI-01 / RF04 | Módulos y pisos en API y MAUI, pertenencia, bajas lógicas, referencias históricas y catálogo real consumido por los incidentes. SySO y Mantenimiento usan cuentas distintas y sus permisos reales. |
| DI-02 / RF05 | Brigadas, tres especialidades confirmadas, integrantes, líderes y varios módulos por brigada. Consulta del líder, nombres y especialidades por módulo; historial conservado. Directorio de cuentas de SA-02 integrado. |
| DI-03 / RF06 | Altas, consulta, modificación y baja lógica de contactos del consultorio, centros médicos y bomberos. No incluye llamadas ni derivaciones del Sprint 3. |
| DI-04 / RF10 | Fichas base relacionadas con incidentes persistidos; cero o varias fichas por caso, creación posterior a atención/evaluación, médico y fecha tomados del servidor, acceso restringido al Médico. |
| DI-05 | Pruebas de integración con identidad, directorio e incidentes reales; falta incorporar lo clínico cuando SySO lo valide y completar la evidencia de dispositivos. |

Las pruebas nuevas ejecutan el pipeline MVC con `DatabaseIdentityContext`, `SessionService`, `BrigadeUserDirectory`, `EfIncidentReader` y las operaciones de Diana. Verifican permisos de los seis roles, bajas de cuentas, sesiones vencidas/cerradas/revocadas, rechazo de cabeceras X-Dev como identidad, brigadas en dos módulos y fichas sin aceptar médico ni fecha falsificados. Una variante repite brigadas y fichas sobre SQL Server migrado; cada prueba SQL usa una base temporal propia.

## Cómo repetir las comprobaciones

- `scripts/windows/Verificar-Diana.cmd`: suite completa con SQL Server LocalDB; crea bases temporales de ensayo.
- `scripts/windows/Compilar-Diana.cmd`: compilación Windows y Android con las herramientas configuradas en el equipo.
- `scripts/windows/Preparar-Android-Diana.cmd [carpetaJdk21]`: prepara dependencias y compila Android. Usa `JAVA_HOME` o el JDK portable de la carpeta de trabajo si no se proporciona una ruta. La opción `--aceptar-licencias [carpetaJdk21]` solo se usa después de que quien ejecuta el comando revise y acepte la licencia del SDK.

La compilación Android verificada utilizó Microsoft OpenJDK 21.0.13.1, cuyo ZIP oficial se contrastó con su SHA-256 publicado. No se guarda el JDK en Git ni se cambia el Java global del equipo. La descarga de herramientas y la aceptación de licencia se realizaron de forma explícita para preparar la compilación.

## Pendientes explícitos

- Recorrer las pantallas con las cuentas reales en Android/iOS. Alejandro coordina la comprobación iOS desde macOS; Diana corrige cualquier defecto de su módulo que se encuentre.
- SySO debe definir campos e informe clínico, visibilidad para encargados y contingencia ante ausencia del médico. Según el reparto, Diana da seguimiento a esa validación. RF10 clínico no se declara completamente validado ni se inventan campos.
- Turnos, recepción de avisos, AUTO y relevo pertenecen a Jorge. La consulta real de líder de Diana está disponible; esta entrega no implementa esos servicios ajenos ni valida el flujo completo del equipo.