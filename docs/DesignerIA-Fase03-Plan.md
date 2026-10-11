# DesignerIA — Fase 3: Plan de generación completa

Fecha: 10 de octubre de 2026.

Referencias: [Contexto](DesignerIA-Contexto.md), [Roadmap](DesignerIA-Roadmap.md) y [Contrato de fase 2](DesignerIA-Fase02-Contrato.md).

## Estado de este documento

Es el plan revisado para implementar la fase 3. **La fase 3 no está iniciada**: no hay código, build ni pruebas de este plan. El contrato que implementa está en el documento de fase 2 y no se repite aquí.

## Objetivo

Galería, formulario, resumen estructural y generación determinística: el formulario de la plantilla «Consulta con grilla» produce el script completo de una vista nueva, o un rechazo sin SQL parcial. La fase 3 no ejecuta escritura.

## Condición previa

**Antes de implementar debe existir una vía comprobada para compilar y ejecutar las pruebas, con responsable definido.** El ejecutor Codex pudo escribir archivos pero no ejecutar Git, build ni pruebas en las sesiones del 9 y 10 de octubre. Esta condición no está cumplida y es el paso 0.

Una implementación no se declara terminada sólo por escribir archivos.

## Pasos y archivos

Se reutilizan los proyectos, servicios y patrones existentes. No se agregan proyectos, dependencias ni capas.

| Paso | Área | Archivos | Cambio |
|---|---|---|---|
| 0 | Condición | — | Comprobar la vía de build y pruebas, y registrar quién la ejecuta |
| 1 | Pruebas | `DesignerIA.Api.Tests/ViewScriptGenerationTests.cs` (nuevo) | Caracterizar la vista mínima vacía actual antes de modificar el generador; sin SQL ni `KnowledgeSource` |
| 2 | Contracts | `DesignerIA.Contracts/ViewSpec.cs`; `GridQueryTemplateRequest.cs` (nuevo); `ViewCreationPreflightResult.cs` | Campos opcionales al final; DTO de cuatro campos del formulario; huecos y datos resueltos del preflight |
| 3 | Plantilla y preset | `DesignerIA.Api/Services/ViewScripts/GridConfigurationPresets.cs` y `Services/ViewTemplates/GridQueryTemplate.cs` (nuevos) | Constante única del preset; el formulario produce el ViewSpec con los nombres acordados |
| 4 | Normalización | `Services/ViewSpecs/ViewSpecNormalizer.cs` | Conservar `ViewType`; no inventar nombres |
| 5 | Validaciones | `Services/ViewSpecs/ViewSpecValidator.cs`; `Services/ViewScripts/ViewScriptGenerationValidator.cs` | Estados duplicados y comparación única; dos formas generables; longitudes; forma del SP; preset conocido; sin handlers |
| 6 | Preflight | `Services/StringConnectionCatalogService.cs`; `Services/ViewScripts/ViewCreationPreflightService.cs`; `appsettings.json` | Alias inexistente, ambiguo o sin conexión; existencia, parámetros y primer resultado del SP; mapeo `GestionEngine` → `GestionEngineLocal`, sin secretos |
| 7 | Plan físico | `Services/ViewScripts/ViewCreationPlan.cs`; `ViewCreationPlanFactory.cs` | Parte opcional de grilla; tipo de vista; SP canónico |
| 8 | Generador | `Services/ViewScripts/ViewScriptGenerator.cs`; `SqlLiteralEscaper.cs` | Una sola entrada pública; diez inserts; literales Unicode y ANSI; estructura transaccional del contrato |
| 9 | Controllers | `Controllers/ViewScriptController.cs`; `Controllers/ViewTemplateController.cs` (nuevo) | Camino único sin atajo al plan; `POST api/view-templates/grid-query` devuelve el `ViewSpecResult` existente |
| 10 | UI | `DesignerIA.Web/Pages/Home.razor`; `wwwroot/css/app.css` | «Nueva vista», galería de una tarjeta y formulario con el patrón de modal actual; el resultado alimenta «Vista en progreso» y se reutiliza «Generar script»; resumen ampliado con tipo, nombres, SP, alias y preset |
| 11 | Verificación | — | Build, pruebas, HTTP en instancia propia y revisión visual; reporte de lo validado y de lo no validado |

Notas de alcance por capa:

- **Controllers delgados:** validación de transporte, llamada a servicios y respuesta. Sin SQL ni reglas.
- **Preflight:** sólo consultas de lectura parametrizadas. No se descubren servidores ni bases.
- **Web:** llamadas HTTP y presentación. El formulario no construye el ViewSpec; lo hace la API.
- **Copilot:** el prompt y el esquema que se le entregan no cambian en esta fase.
- **`appsettings.json`:** el mapeo de alias a nombre de conexión no contiene cadenas de conexión ni secretos.

## Pruebas

Automáticas, sin base de datos y sin `KnowledgeSource`:

- Vista mínima vacía: cuatro registros y valores, antes y después de los cambios intencionales.
- Grilla: diez inserts en orden y enlazados por variables.
- Ausencia de `ParametrosSP`, `MapeoParametros`, `WebControls` y `Handlers` en el script.
- Preset idéntico a la constante, comprobado por huella.
- Texto con comillas y acentos, con prefijo `N`.
- Rechazo sin script: pendientes, forma no soportada, preset desconocido, handlers presentes, más de una fila o área.
- Casos de forma del SP: con y sin corchetes, sin esquema, tres partes, caracteres no admitidos.
- Estado inicial con otra capitalización y estados duplicados.
- `ViewType` tras normalizar y tras la serialización HTTP.
- La plantilla produce el JSON de ejemplo del contrato.
- Traducción de cada resultado del preflight a su rechazo.
- Ausencia de construcciones vetadas para SQL Server 2008 R2 en el texto generado.

Verificación proporcional con la aplicación en ejecución:

- `POST api/view-templates/grid-query` y `POST api/view-script/generate` con el SP de prueba.
- Colisión contra un nombre existente.
- Revisión visual de galería, formulario, resumen y script, con el aviso «Script generado. No ejecutado.» intacto.

## Criterios de aceptación

- Script completo o rechazo; nunca SQL parcial.
- Diez registros con sus relaciones según el contrato.
- Configuración de grilla literal.
- Unicode conservado donde las columnas lo permiten.
- Colisión rechazada en el preflight y protegida en el script.
- La vista mínima vacía conserva su comportamiento y sus cuatro registros.
- Comprobación HTTP y visual realizada y reportada.

## Fuera de fase 3

- Ejecutar el script o cualquier SQL de escritura.
- Pruebas de concurrencia, reversión, cancelación y comprobación en Motor (fase 4).
- Creación automática, traducción de errores al usuario y URL (fase 5).
- Selector de alias, buscador de SP y medición de ahorro.
- Parámetros, filtros, navegación, gráficos y JS (fase 6).
- Cambios al prompt de Copilot y entrada por lenguaje natural o Word (fase 7).

Toda ampliación se presenta al usuario antes de introducirla.

## Límites de autorización

La implementación de fase 3 no autoriza por sí misma Git de escritura, SQL de escritura, dependencias nuevas, cambios de autenticación, Azure ni despliegue. Cada una requiere autorización explícita del usuario, según `AGENTS.md`.

## Checkpoint para los chats de fase 3

```text
Fase activa: 3 — Generación completa.
Entrega puntual: pasos 0 a 11 de este plan.
Estado del repositorio: recontrastar rama, commit y árbol al iniciar.
Archivos involucrados: los de la tabla de pasos.
Qué ya se comprobó: contrato y plan revisados; nada implementado ni ejecutado.
Qué queda pendiente: condición de build y pruebas; decisión opcional sobre
  caracteres del nombre de vista; todas las comprobaciones de la sección 10
  del contrato.
Acciones autorizadas para este encargo: las que indique el usuario al iniciar.
```
