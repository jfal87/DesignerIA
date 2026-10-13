# DesignerIA — Checkpoint de inicio de fase 3

Fecha: 10 de octubre de 2026. Contexto breve para los chats nuevos de Claudio y del revisor. No requiere transportar conversaciones anteriores.

## Estado

- **Fase 2 cerrada documentalmente.** Contrato y plan revisados y aprobados por el revisor.
- **Fase 3 pendiente de iniciar.** No hay código, build ni pruebas de la plantilla «Consulta con grilla».
- Nada del contrato está implementado ni comprobado en runtime. No se generó ni aplicó ningún script y no se comprobó nada en Motor.

## Documentos y orden de lectura

1. `AGENTS.md`, en la raíz del repositorio.
2. [Contexto](DesignerIA-Contexto.md).
3. [Roadmap](DesignerIA-Roadmap.md).
4. [Fase 1: Alcance](DesignerIA-Fase01-Alcance.md).
5. [Fase 2: Contrato](DesignerIA-Fase02-Contrato.md).
6. [Fase 3: Plan](DesignerIA-Fase03-Plan.md).

Los documentos contienen el alcance completo; este checkpoint sólo resume el punto de partida.

## Repositorio y rama de referencia

- Repositorio: `C:\Code\DesignerAI`.
- Rama de cierre de fase 2: `feature/grid-query-contract`. El commit de cierre es su `HEAD`; consultarlo con Git de lectura. Su commit padre es el cierre de fase 1, `fd004b7`, en `feature/grid-query-template`.
- **Al comenzar, recontrastar rama, `HEAD` y árbol.** No dar por válido este checkpoint si difieren, y no descartar cambios de otros participantes.
- La rama de fase 3 no está creada. Crearla requiere autorización del usuario.

## Qué quedó aprobado

- **Contrato:** ampliación de `ViewSpec` con campos opcionales, preset inmutable `grid-consulta-v1`, los diez registros con sus relaciones, forma almacenada del Stored Procedure, camino único de validación, preflight y diseño transaccional del script.
- **Plan de fase 3:** pasos 0 a 11, pruebas y criterios de aceptación.
- **`UsaSeguridad = 0`:** confirmado por el usuario para las vistas nuevas de esta entrega.
- **Nombres:** `Estado01`, `Fila01`, `Area01` y `Grilla01`; no se reabren.
- **Primer recorrido:** SP de prueba `dbo.PRC_GET_GRID_TEST_SORT_COLUMN` en GestionEngine local, con el alias `GestionEngine` mapeado a la conexión `GestionEngineLocal`. La clave de ese mapeo se crea en fase 3.

Decisión abierta y opcional, del usuario: restringir los caracteres del nombre de vista. Sin decisión no se aplica ninguna restricción adicional.

## Responsabilidades

| Participante | Responsabilidad |
|---|---|
| Usuario y chat revisor | Dialogar, revisar propuestas y resultados, resolver decisiones y comprobar el cierre. El revisor no implementa ni modifica Git |
| Claudio | Consolidar el plan, coordinar ramas y encargos al ejecutor, revisar la ejecución y coordinar los commits de cierre |
| Ejecutor | Realizar únicamente el encargo recibido y reportar cambios, verificaciones y pendientes |

El usuario transporta los resultados entre chats. No asumir memoria compartida ni enviar mensajes a otros chats por iniciativa propia.

## Paso 0: vía de compilación y pruebas

**Antes de implementar debe comprobarse una vía para compilar y ejecutar las pruebas, con responsable definido.** La condición no está cumplida.

Antecedentes, que no la dan por resuelta: el 9 de octubre Claudio compiló y ejecutó pruebas desde su sesión; el ejecutor Codex pudo escribir archivos pero no ejecutar Git, build ni pruebas en sus sesiones del 9 y 10 de octubre.

No declarar una implementación terminada sólo por escribir archivos, ni modificar configuración global para resolver esta condición por iniciativa propia.

## Alcance de fase 3

- Galería con la plantilla «Consulta con grilla», formulario de cuatro campos y resumen estructural.
- Generación determinística: script completo de una vista nueva, o rechazo sin SQL parcial.
- La vista mínima vacía conserva su comportamiento y sus cuatro registros.
- **Sin ejecutar escritura:** no se aplica el script ni ningún SQL de escritura.

Quedan fuera: selector de alias, buscador de SP, medición, cambios al prompt de Copilot, creación automática y URL.

## Comprobaciones pendientes de fase 4

Ninguna está hecha. El detalle está en las secciones 6 y 10 del contrato.

- Aplicación autorizada del script, en sesión dedicada y sin transacción previa.
- Coherencia de los diez registros y de sus relaciones.
- Concurrencia entre creaciones y con un guardado simultáneo de Designer.
- Reversión, espera de bloqueo agotada y cancelación.
- Compatibilidad del script con SQL Server 2008 R2.
- **Motor:** render de la grilla y comportamiento del parámetro `@QueryString`. El código de Motor no está en el repositorio, su URL base sigue sin definir y el acceso al ambiente del cliente no está disponible.
- Compatibilidad al abrir y guardar la vista en Designer.

## Publicación de las ramas

Comprobado el 10 de octubre de 2026, al preparar este archivo, con una consulta de lectura al remoto `origin`:

| Rama | Estado |
|---|---|
| `main`, `develop`, `feature/chat-markdown`, `feature/knowledge-improvements` | Publicadas |
| `feature/grid-query-template` (cierre de fase 1) | **Sólo local** |
| `feature/grid-query-contract` (cierre de fase 2) | **Sólo local** |

No hay merge ni pull request de estas dos ramas. `develop` no contiene `feature/knowledge-improvements`, sobre la que se apoyan. Recontrastar este estado al comenzar: puede haber cambiado.

## Autorizaciones

Este checkpoint no autoriza nada. Implementar, crear ramas, hacer commit, publicar, integrar, ejecutar SQL de escritura, agregar dependencias o desplegar dependen del encargo explícito del usuario en el chat nuevo y de `AGENTS.md`.

Lectura de archivos y Git de lectura pueden hacerse sin preguntar. Las consultas SQL de lectura requieren que el usuario autorice servidor, base y autenticación para el encargo.
