# RF10 / DI-05: plantilla clínica provisional con datos de prueba

Se implementa por la instrucción comunicada por Diana el 22/11/2026: su jefe indicó resolver el pendiente de SySO con datos de prueba y dejar las comprobaciones de dispositivos y flujo conjunto para otro día. **No constituye validación clínica de SySO ni habilita el uso de datos reales.**

## Plantilla de ensayo SYSO-ENSAYO-v1

| Campo | Regla del prototipo | Ejemplo ficticio |
| --- | --- | --- |
| Alias de persona | Obligatorio únicamente al adjuntar la plantilla de ensayo; máximo 100 caracteres. No solicita documento ni identidad real. | PERSONA-DEMO-001 |
| Edad de ensayo | Opcional; entero entre 0 y 120. | 22 |
| Observaciones | Texto de prueba obligatorio para la plantilla; máximo 1500 caracteres. | Observación ficticia para comprobar la ficha. |
| Atención registrada | Texto de prueba obligatorio para la plantilla; máximo 1500 caracteres. No proporciona indicaciones de tratamiento. | Atención de ensayo registrada por el médico. |
| Informe de ensayo | Texto de prueba obligatorio; máximo 1500 caracteres. | Informe de prueba sin validez clínica. No corresponde a una persona real. |
| Marca de prueba | `IsTestData=true`; la API rechaza una plantilla marcada como dato real. | true |
| ID de ficha/incidente, médico y fecha | Se obtienen del caso, sesión y reloj del servidor. | Generados al guardar; no se rellenan con el botón de ejemplo. |

La ficha base sin plantilla continúa permitida, incluyendo registros anteriores y casos sin accidentados. Esta ampliación es opcional y no transforma los campos ficticios en requisitos clínicos definitivos.

## Informe, visibilidad y ausencia del médico

- El Médico puede guardar, listar y abrir el informe. Su encabezado indica **DATOS FICTICIOS — SIN VALIDEZ CLÍNICA**, versión de plantilla, ficha, incidente, médico y fecha, además de los textos de ensayo.
- Decisión provisional de visibilidad: solo Médico. SySO, Mantenimiento y los otros roles no reciben datos de la ficha ni del informe por la API. El prototipo no presume autorización clínica para los encargados.
- Contingencia provisional: si no hay Médico, el sistema no registra la ficha en nombre de otro usuario y no exige una ficha ficticia para continuar el incidente. No se agregan sustituciones de rol, solicitudes ni avisos previos al consultorio. El procedimiento institucional definitivo se valida después con SySO.
- La auditoría conserva actor, operación, fecha y referencia al caso; no copia alias ni textos clínicos.

## Uso en la app de ensayo

1. Aplicar la migración `20261122191445_DianaTestClinicalDraft` después de las migraciones existentes; no elimina datos ni reemplaza las tablas de usuarios/incidentes.
2. Iniciar la API en Development. `Diana:ClinicalPrototype:Enabled=true` está configurado solo en `appsettings.Development.json`; fuera de ese entorno permanece deshabilitado salvo configuración explícita de ensayo.
3. Entrar con una cuenta real de Médico y abrir **Registro de accidentados**. Seleccionar un incidente de ensayo existente.
4. Presionar **Cargar datos ficticios**. Se rellenan los textos y se activa **Agregar ficha clínica de ensayo**; todavía no se guarda ni se confirma atención automáticamente.
5. Confirmar la evaluación/atención de ensayo y presionar **Guardar ficha**. Se puede crear otra ficha para el mismo incidente.
6. Presionar **Ver informe de ensayo** en una ficha que contenga la plantilla. Las fichas base anteriores no muestran ese botón.

API: el POST habitual admite el campo opcional `testClinicalDraft`. El informe se consulta en `GET /api/v1/incidents/{incidentId}/injury-records/{recordId}/test-report` y requiere la misma autorización de Médico que las fichas. No se ofrece un endpoint público de datos clínicos ni se crean personas, incidentes o cuentas automáticamente al iniciar.

## Comprobaciones de código

`DianaClinicalPrototypeTests` cubre guardar/listar/informar con datos ficticios, compatibilidad con fichas base, separación de roles, marcador de prueba, límites de edad, confirmación previa y ausencia de datos clínicos en auditoría. Incluye una variante sobre SQL Server con todas las migraciones. Los resultados deben identificarse por versión; los 38 casos aprobados de la entrega anterior no validan automáticamente esta modificación.

La validación clínica real y las comprobaciones Android/iOS y con Jorge quedan diferidas según la instrucción del jefe; no se registran como realizadas.

Resultado de esta versión: `dotnet test` aprobó 36 pruebas y omitió las cinco que requieren SQL Server; no hubo fallos. Se comprobó que el modelo coincide con la migración generada. La API y la app para Windows y Android compilaron con cero errores y cero advertencias. Los resultados están en `Resultados_Diana_Clinica_Prueba`, junto al clon. No se ejecutaron recorridos de interfaz ni se presenta la variante SQL omitida como aprobada.
