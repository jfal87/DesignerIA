# DesignerIA — Checkpoint de inicio de fase 4

Nota de publicacion: los identificadores de infraestructura y cuentas de Windows se sustituyeron por marcadores; el documento original se conserva en el equipo de desarrollo.

Fecha: 10 de octubre de 2026. Contexto breve para los chats nuevos de Claudio y del revisor. No requiere transportar conversaciones anteriores.

## Estado

- **Fase 3 cerrada funcionalmente en el ambiente local.** El formulario de «Consulta con grilla» produce el script completo de una vista nueva o un rechazo sin SQL parcial. Detalle en el [informe de fase 3](DesignerIA-Fase03-Informe.md).
- **Fase 4 pendiente de iniciar.** El script generado nunca se ejecutó: no hay metadata aplicada ni comprobación en Motor.
- La creación automática desde la API y la URL son de fase 5. «Crear vista» sigue deshabilitado.

## Documentos y orden de lectura

1. `AGENTS.md`, en la raíz del repositorio.
2. [Contexto](DesignerIA-Contexto.md).
3. [Roadmap](DesignerIA-Roadmap.md).
4. [Fase 2: Contrato](DesignerIA-Fase02-Contrato.md), en especial las secciones 5, 6 y 10.
5. [Fase 3: Informe](DesignerIA-Fase03-Informe.md).

El plan y el checkpoint de inicio de fase 3 quedan como registro fechado.

## Repositorio y rama de referencia

- Repositorio: `C:\Code\DesignerAI`.
- Rama de cierre de fase 3: `feature/grid-query-generation`. Parte del cierre de fase 2, `bd36385`, en `feature/grid-query-contract`.
- Este archivo se redactó antes de los commits de cierre de fase 3. Una vez hechos, el `HEAD` de la rama debe ser el commit de cierre documental, precedido por el de implementación; comprobarlo con Git de lectura.
- **Al comenzar, recontrastar rama, `HEAD` y árbol.** No dar por válido este checkpoint si difieren, y no descartar cambios de otros participantes.
- La rama de fase 4 no está creada. Crearla requiere autorización del usuario.

## Qué deja resuelto la fase 3

- **Generación:** una sola entrada validada; diez INSERT para la grilla y cuatro para la vista mínima vacía; estructura transaccional de la sección 5 del contrato.
- **Preflight:** seis comprobaciones por lectura de metadata, sin ejecutar el Stored Procedure. Una grilla exige las seis y el SP canónico.
- **UI:** galería, formulario de cuatro campos, resumen y panel de script con el aviso «Script generado. No ejecutado.».
- **Pruebas:** 649 superadas, todas locales. Compilación y pruebas a cargo de Claudio, con `--artifacts-path C:\Code\DesignerAI\artifacts`.
- **Recorrido comprobado:** SP `dbo.PRC_GET_GRID_TEST_SORT_COLUMN` con alias `GestionEngine` en `SQL_LOCAL_HOST / GestionEngine`, autenticación Windows.

Las precisiones adoptadas durante la implementación están en la sección 3 del informe y no se reabren.

## Qué necesita la fase 4

La fase 4 tiene dos checkpoints distintos: **metadata local comprobada** y **Motor comprobado**. El primero no acredita el segundo ni cierra por sí solo la fase.

Para la metadata local:

- **Autorización explícita de escritura** del usuario para aplicar el script, con servidor, base y autenticación. Sin ella no se ejecuta nada.
- Seguir el procedimiento de la sección 6 del contrato: sesión dedicada, sin transacción previa, máximos de los ocho IDs registrados antes, un solo batch y verificación desde otra sesión.
- Generar el script en el momento de aplicarlo: el preflight vale para el ambiente y el instante en que se ejecutó.

Para Motor:

- La URL base publicada de Motor no está definida y el acceso al ambiente del cliente no está disponible. No inventar un host.
- El código de Motor no está en el repositorio.

## Comprobaciones pendientes

Ninguna está hecha. El detalle está en la sección 10 del contrato.

- Aplicación autorizada del script y coherencia de los diez registros y sus relaciones.
- Colisión por `Nombre` con `IdVista` distinto.
- Concurrencia: dos creaciones con nombres distintos, dos con el mismo nombre y una creación simultánea con un guardado estilo Designer.
- Reversión forzada antes del `COMMIT`, espera de bloqueo agotada y cancelación a mitad del script.
- Lectura de `Vistas` durante la espera.
- Compatibilidad del script con SQL Server 2008 R2. La base local es SQL Server 2022.
- **Motor:** render de la grilla, efecto de `UnidadAltoFila` = `817`, `@QueryString` sin mapeo y `UsaSeguridad = 0`.
- Compatibilidad al abrir y guardar la vista en Designer.

## Límites heredados de fase 3

- La limpieza del script cuando el chat sustituye la vista en progreso se revisó sólo por lectura.
- «Alias ambiguo», «Alias no es SQL Server», «SP con parámetros no admitidos por la plantilla», «Resultado incompatible» y «Resultado no determinable» sólo están cubiertos por pruebas unitarias.
- Dos observaciones menores de presentación, sin corregir: un alias inexistente muestra dos rechazos, y con el nombre vacío el título del resumen queda en blanco.
- `Vistas.JsCode` es `varchar(max)` y recibe `N''`; se mantuvo por ser una cadena vacía.

## Cómo levantar la demo local

Con los puertos 7089 y 7276 libres y la solución compilada con `--artifacts-path`, en dos terminales desde la raíz del repositorio:

```text
dotnet run --project DesignerIA.Api --launch-profile https --no-build --artifacts-path C:\Code\DesignerAI\artifacts
dotnet run --project DesignerIA.Web --launch-profile https --no-build --artifacts-path C:\Code\DesignerAI\artifacts
```

Abrir `https://localhost:7276` y pulsar «+ Nueva vista». Gestionar únicamente los procesos propios y detenerlos al terminar.

## Responsabilidades

| Participante | Responsabilidad |
|---|---|
| Usuario y chat revisor | Dialogar, revisar propuestas y resultados, resolver decisiones y comprobar el cierre. El revisor no implementa ni modifica Git |
| Claudio | Consolidar el encargo, coordinar ramas y ejecutor, compilar y probar, revisar la ejecución y coordinar los commits de cierre |
| Ejecutor | Realizar únicamente el encargo recibido y reportar cambios, verificaciones y pendientes |

El usuario transporta los resultados entre chats. No asumir memoria compartida ni enviar mensajes a otros chats por iniciativa propia.

## Publicación de las ramas

Según la configuración local al preparar este archivo. **La publicación no se reverificó contra el remoto.**

| Rama | Estado local |
|---|---|
| `main`, `develop`, `feature/chat-markdown`, `feature/knowledge-improvements` | Con upstream configurado |
| `feature/grid-query-template` (fase 1), `feature/grid-query-contract` (fase 2), `feature/grid-query-generation` (fase 3) | **Sin upstream configurado** |

No se comprobó en el remoto si existen ramas, merges o pull requests de estas tres ramas. Recontrastar este estado al comenzar: puede haber cambiado.

## Evidencia local

Las capturas, el script generado y las respuestas HTTP de la demostración están en `artifacts/`, carpeta ignorada por Git. No forman parte del repositorio y pueden no existir en otro equipo.

## Autorizaciones

Este checkpoint no autoriza nada. Aplicar el script, ejecutar SQL de escritura, crear ramas, hacer commit, publicar, integrar, agregar dependencias o desplegar dependen del encargo explícito del usuario en el chat nuevo y de `AGENTS.md`.

Lectura de archivos y Git de lectura pueden hacerse sin preguntar. Las consultas SQL de lectura requieren que el usuario autorice servidor, base y autenticación para el encargo.
