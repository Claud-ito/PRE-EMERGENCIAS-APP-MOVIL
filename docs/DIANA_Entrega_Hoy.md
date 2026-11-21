# Cierre de Diana hasta Sprint 2

## Qué está preparado sin implementar tareas de otros integrantes

| RF | Implementación de Diana | Evidencia disponible | Dependencia restante |
| --- | --- | --- | --- |
| RF04 | API y pantallas de módulos/pisos; bajas lógicas; catálogo consumido por incidentes. | Persistencia SQLite, validaciones de pertenencia y flujo con AL-01. | Inicio de sesión real y comprobación móvil. |
| RF05 | Brigadas, tres especialidades confirmadas, integrantes, líderes, varios módulos; consulta visible de líder por módulo e historial. | Pruebas de alta, cambio de líder/módulos, desactivación, cuenta inactiva, especialidad inválida y módulo duplicado. | Directorio de usuarios SA-02, sesión SA-01 y comprobación móvil. |
| RF06 | Consultorio, centros médicos y bomberos; altas, edición y desactivación conservando referencias. | Persistencia y baja lógica comprobadas. | Sesión real y comprobación móvil. |
| RF10 | Fichas base por incidente real; FK; cero o varias fichas; selección mínima del caso por Médico. | Integración con AL-01, rechazo de fichas huérfanas y permisos. | Sesión Médico; campos y visibilidad clínica de SySO; comprobación móvil. |

Las cuentas de ensayo de las pruebas están únicamente en el proyecto de pruebas. No se instala otro sistema de usuarios ni se amplían permisos X-Dev en la API o en MAUI.

## Comprobación aprobada con SQL Server real

En Windows con LocalDB y las dependencias restauradas, abrir `scripts/windows/Verificar-Diana.cmd`.
Ejecuta las pruebas habituales con `UNIVALLE_TEST_SQLSERVER` apuntando a `(localdb)\MSSQLLocalDB`.
Las pruebas SQL crean y eliminan exclusivamente sus bases temporales, con nombres propios de ensayo; no usan la base de desarrollo del proyecto.
El resultado se escribe en la carpeta `Resultados_Diana_SQLServer`, junto al repositorio: `salida.txt` y `Diana_SQLServer.trx`.
Un error de LocalDB o del testhost debe resolverse antes de declarar esta comprobación aprobada.

Resultado del 20/11/2026: Diana ejecutó el script en su terminal Windows. Se revisaron `salida.txt` y los contadores de `Diana_SQLServer.trx`: **27 pruebas ejecutadas, 27 aprobadas, 0 errores y 0 omitidas**, incluyendo los tres escenarios de SQL Server. La ejecución habitual de `dotnet test` y las pruebas con LocalDB quedan verificadas.

## Verificación disponible

- Compilación de API, contratos y pruebas sin errores ni advertencias con los paquetes reales 10.0.15.
- 24 escenarios Fact ejecutados directamente y aprobados con aserciones xUnit/SQLite.
- Posteriormente, 27 pruebas aprobadas mediante `dotnet test` en la terminal de Diana, incluidas dos de concurrencia de Alejandro y una de migración/brigadas/historial de Diana con SQL Server LocalDB.
- Las pruebas de interfaz en Android/iOS siguen pendientes y requieren la integración de sesión real.

## Qué debe probarse al llegar Said

1. SySO y Mantenimiento entran con cuentas distintas y pueden operar catálogos sin depender del turno.
2. Asignar usuarios reales a una brigada y dos módulos; consultar el líder desde cada módulo.
3. Cambiar líder, desactivar una cuenta y revisar que se conserve el historial sin devolver un líder inactivo.
4. Médico consulta un incidente y crea varias fichas base. Los encargados no reciben acceso a fichas por su rol.
5. Probar estas acciones en Android/iOS con la API y SQL Server de pruebas.

La plantilla clínica y la visibilidad para encargados continúan explícitamente pendientes en el Excel. La integración de AUTO pertenece a Jorge. Esos pendientes no se presentan como terminados por completar el código de Diana.
