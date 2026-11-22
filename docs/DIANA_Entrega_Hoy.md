# Cierre de Diana hasta Sprint 2

## Integración con Said sobre main 03d9000

El PR #2 de Diana ya está unido a `main`. Said incorporó identidad, sesiones y usuarios reales: `DatabaseIdentityContext`, `BrigadeUserDirectory` y el adaptador MAUI `DianaMobileSession`. Este último se registra antes de `AddDianaMobile()`, de modo que se utiliza la sesión compartida y no el registro provisional. La navegación común muestra las operaciones según los permisos. **Said ya no es una dependencia de código pendiente de Diana.**

Se añadieron pruebas HTTP de MVC con los servicios reales de identidad y directorio, sin sustituir cuentas por cabeceras X-Dev:

- SySO y Mantenimiento operan módulos, pisos y contactos con cuentas separadas; otros roles reciben 403.
- Una cuenta creada mediante SA-02 integra una brigada con dos módulos. La consulta devuelve el líder, nombres y especialidades; desactivarlo deja de devolverlo sin borrar las asignaciones anteriores.
- El médico selecciona un incidente persistido y registra cero o varias fichas. La API obtiene médico y fecha de la sesión/reloj, ignorando valores falsificados del formulario. Los demás roles no acceden a las fichas.
- Una sesión ausente, vencida, cerrada o revocada por desactivación recibe 401. Las cabeceras de desarrollo no reemplazan una sesión real.

Las cuatro pruebas nuevas se aprobaron con SQLite y el pipeline MVC en memoria (34 pruebas aprobadas en esa ejecución). Los tres casos SQL anteriores fallaron al intentar abrir LocalDB desde el entorno de trabajo restringido: error 50/0x89c50118; no se atribuye ese fallo a las reglas funcionales. Después se agregó un quinto escenario que ejecuta brigadas y fichas con cuentas reales sobre SQL Server migrado. La suite actual contiene **38 pruebas**, incluidas cuatro dependientes de SQL Server; su repetición final queda preparada para la terminal Windows.

La compilación de los cambios de pruebas terminó, pero la siguiente ejecución del testhost fue bloqueada por Control de aplicaciones de Windows (0x800711C7). No se desactiva esa protección. Después, Diana ejecutó `scripts/windows/Cerrar-Integracion-Diana.cmd` en su terminal Windows: se revisaron `cierre_sql.txt` y los contadores de `Cierre_Diana_Said.trx`: **38 ejecutadas, 38 aprobadas, 0 fallidas, 0 errores y 0 omitidas**, incluidos los cuatro escenarios SQL Server y el nuevo flujo de cuentas reales, brigadas y fichas sobre base migrada. Esta ejecución nativa supera los bloqueos anteriores como evidencia de las pruebas.

La compilación Windows/Android sigue pendiente: ambos pasos del script se detuvieron durante la restauración por **NETSDK1147: falta `maui-android`** en el equipo. No es una compilación aprobada ni demuestra un fallo del código de Diana. Instalar las cargas requeridas con `dotnet workload restore PRE-EMERGENCIAS-APP-MOVIL/PRE-EMERGENCIAS-APP-MOVIL.csproj` y repetir la compilación. Los informes están en `Resultados_Diana_Integracion` junto al repositorio.

La evidencia de Said sobre Windows/Android está en `docs/evidencias/SA01_SA03_Verificacion.md`; es evidencia de su revisión, no una nueva prueba de dispositivo de Diana. Continúan pendientes el recorrido funcional de estas pantallas en Android/iOS y los campos clínicos/visibilidad que valide SySO. El reparto asigna a Diana el seguimiento de esa validación. Jorge sigue siendo responsable de turno, avisos, AUTO y relevo, que no se implementan en esta entrega.

Las secciones posteriores conservan la evidencia anterior del 20/11/2026; las referencias a esperar a Said quedan superadas por esta integración.

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
