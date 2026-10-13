# DesignerIA — Inventario de reutilización y limpieza aplicada

Fecha: 11 de octubre de 2026, segunda revisión.

## Estado: limpieza **aplicada y validada**

Se retiró el flujo de formulario y galería y lo que dependía sólo de él. No se sustituyó `ViewSpec` ni se implementó nada de la carga JSON. Sin escrituras Git, sin SQL de escritura, sin dependencias nuevas, sin cambios de autenticación ni de configuración.

En el primer intento (primera revisión) el control automático de permisos rechazó el borrado de archivos. En este segundo intento no hubo ningún bloqueo.

## 1. Inventario actualizado

Rutas relativas a `C:\Code\DesignerAI`.

### Retirado

| Archivo / responsabilidad | Qué era | Motivo | Dependencias resueltas |
|---|---|---|---|
| `DesignerIA.Web/Pages/Home.razor` — botón «+ Nueva vista», modal de galería y formulario «Consulta con grilla», filas de parámetros, `AbrirNuevaVista` … `PrepararVistaAsync`, `ParametroFijo`, campos `_*NuevaVista` | Formulario de fases 3 y 6.1 | Lo sustituye la carga de JSON | Era el único consumidor de `api/view-templates/grid-query` |
| `DesignerIA.Api/Controllers/ViewTemplateController.cs` | `POST api/view-templates/grid-query` | Sólo lo llamaba el formulario | Sin otras referencias |
| `DesignerIA.Api/Services/ViewTemplates/GridQueryTemplate.cs` y su carpeta | Armaba el ViewSpec `Estado01/Fila01/Area01/Grilla01` | El documento traerá la estructura | Controller y cuatro puntos de pruebas, adaptados |
| `DesignerIA.Contracts/GridQueryTemplateRequest.cs` | DTO del formulario | Sin consumidores | Home, controller y plantilla, retirados |
| `DesignerIA.Web/Components/ViewSpecPreview.razor`, su carpeta, las pestañas «Conversación / Vista previa», `_mostrarVistaPrevia` y `@using DesignerIA.Web.Components` | Vista previa simulada | Ver «Vista previa» abajo | Único uso: `Home.razor` |
| `DesignerIA.Web/wwwroot/css/app.css` — `.workspace-tab*`, `.chat-conversation*`, `.viewpreview*`, `.view-template-*`, `.modal-parameter-*` | 235 líneas | Sólo las usaban las piezas anteriores | Búsqueda de clases: sin otros usos |

### Adaptado

| Archivo | Cambio | Motivo |
|---|---|---|
| `DesignerIA.Api.Tests/ViewScriptGenerationTests.cs` | Se quita `using …ViewTemplates` y una aserción sobre la plantilla; `GridSpec` y `ExampleSpec` construyen el mismo ViewSpec con un auxiliar local `GridViewSpec`; dos pruebas cambian de nombre (`GridSpecMatchesContractExampleJson`, `NullAndEmptyParametersKeepTheCurrentGridScript`) | La plantilla ya no existe. Mismo número de pruebas: 43 atributos y 141 `InlineData` antes y después |

### Vista previa simulada: dependencias

- La consumía sólo `Home.razor`, que le pasaba `_vistaEnProgreso.Spec`.
- Ese ViewSpec procede del formulario (retirado) o del chat (conservado). **No pertenecía en exclusiva al flujo retirado:** también dibujaba una vista preparada por el chat.
- Se retiró por tres razones: no existía en la base del RAG (`74ca306`); envolvía la conversación en pestañas, es decir, modificaba la pantalla del chat; y no tiene destino en el nuevo recorrido, porque dibuja `ViewSpec` con columnas ficticias y la revisión del JSON se hará sobre el documento.
- Efecto para el usuario: una vista preparada por el chat se sigue viendo en el panel «Vista en progreso»; desaparece la pestaña «Vista previa».
- Si se quiere de vuelta: `artifacts/limpieza-20261011/antes/DesignerIA.Web/Components/ViewSpecPreview.razor` y los bloques de `Home.razor` y `app.css` de la misma carpeta.

### Conservado, sin cambios en esta limpieza

| Archivo / responsabilidad | Destino concreto |
|---|---|
| Preparación por lenguaje natural: `CopilotChatService`, `ViewSpecResponseParser`, `ViewSpecNormalizer`, `ViewSpecValidator`, `ConversationTurnCompletion`, router | Se conserva tal cual. Migración descrita en `DesignerIA-JSONMetadata-Contrato.md`, sección 7 (pasos T2 y T3) |
| `Contracts/ViewSpec*.cs`, `ChatResult.cs` | Contratos compartidos con el chat: no se retiran mientras tengan consumidores |
| Panel «Vista en progreso», «Conocimiento faltante → Enseñar a DesignerIA», «Laboratorio de conocimiento», «Actualizar conocimiento» | RAG y conocimiento curado |
| «Generar script», «Crear vista» con confirmación y panel «Creación de vista» en `Home.razor`; `ViewScriptController` | Revisión, creación y resultado; hoy reciben el ViewSpec del chat y recibirán el borrador del documento |
| `StringConnectionCatalogService` | Resolver cada alias del documento |
| `ViewCreationPreflightService` | Validación de catálogo y ambiente |
| `ViewScriptGenerationValidator` | Reglas de texto, SP, parámetros y formas soportadas |
| `ViewCreationPlan`, `ViewCreationPlanFactory`, `ViewScriptGenerator`, `GridConfigurationPresets`, `SqlLiteralEscaper` | Plan físico y SQL de la grilla, que es la primera entrega |
| `ViewCreationService`, `ViewCreationVerifier`, `ViewCreationResult` | Ejecución, verificación y resultado. La entrada de `ViewCreationService` (hoy `ViewSpec`) se adaptará en la primera entrega |
| Fase 6.1 sin confirmar: parámetros con valor fijo | `source.kind = fixed` de la primera entrega |
| `appsettings.json`, `appsettings.Development.example.json`, `Program.cs`, autenticación | Sin cambios |
| `KnowledgeSource/`, índice de conocimiento, conocimiento curado, datos, vistas y SP de las bases | No se tocaron |

Ningún servicio se conserva por utilidad hipotética: todos los de creación están en el camino de la grilla, que es la primera entrega.

## 2. Diff aplicado

Medido contra las copias previas a la limpieza.

| Archivo | Cambio |
|---|---|
| `DesignerIA.Web/Pages/Home.razor` | −188 líneas, +0 |
| `DesignerIA.Web/wwwroot/css/app.css` | −235 líneas, +0 |
| `DesignerIA.Web/_Imports.razor` | −1 línea |
| `DesignerIA.Api.Tests/ViewScriptGenerationTests.cs` | −9, +16 |
| `DesignerIA.Api/Controllers/ViewTemplateController.cs` | Eliminado (24 líneas) |
| `DesignerIA.Api/Services/ViewTemplates/GridQueryTemplate.cs` | Eliminado (17 líneas) |
| `DesignerIA.Contracts/GridQueryTemplateRequest.cs` | Eliminado (8 líneas) |
| `DesignerIA.Web/Components/ViewSpecPreview.razor` | Eliminado (148 líneas) |

Los otros 16 archivos que ya tenían cambios sin confirmar quedaron idénticos a su copia (`cmp`). No quedan referencias a las piezas retiradas en `.cs`, `.razor`, `.css`, `.json`, `.csproj` ni `.slnx`.

Comprobación de la pantalla del chat: en `git diff 74ca306 -- DesignerIA.Web/Pages/Home.razor`, la primera diferencia aparece ya dentro del panel «Vista en progreso». La barra de navegación y la conversación coinciden con la base del RAG.

## 3. Recuperación

Creada antes de editar y comprobada otra vez al empezar esta revisión, en `artifacts/limpieza-20261011/` (ignorada por Git, fuera del código productivo):

| Archivo | Contenido |
|---|---|
| `HEAD.txt`, `git-status-antes.txt` | `28532c2`, rama y los 20 archivos modificados |
| `cambios-sin-confirmar-antes.patch` | `git diff --binary HEAD` previo: todo el trabajo sin confirmar, incluida la fase 6.1 |
| `antes/` | Copia íntegra de 24 archivos |
| `antes.sha256` | SHA-256 de cada copia |
| `pruebas/limpieza.trx` | Resultado de las pruebas posteriores |

Comprobado antes de editar: `sha256sum -c` sin errores y las 24 copias idénticas a los archivos de trabajo. Para deshacer: copiar `antes/` sobre el repositorio. No requiere `reset`, `stash`, `clean` ni cambio de rama.

## 4. Validaciones realizadas

### Compilación y pruebas

| Comprobación | Resultado |
|---|---|
| `dotnet build DesignerIA.slnx --artifacts-path …\artifacts` | 0 errores, 17 advertencias (el mismo número que registra el checkpoint de fase 5) |
| `dotnet test` de `DesignerIA.Api.Tests` | **857 superadas, 0 con error, 0 omitidas.** Locales, sin SQL ni Copilot |

Pruebas por área:

| Área | Clases | Pruebas |
|---|---|---|
| Errores y fallos del planner | `PlannerFailureRegressionTests`, `PlannerContractRegressionTests`, `PlannerClarificationRoutingTests` | 279 |
| Conversación, seguimiento y enrutamiento | `ConversationCapabilityRouterTests`, `ConversationSubjectResolverTests`, `ConversationPlanningTests`, `ConversationIntegrationTests` | 182 |
| Wiki, evidencia y fuentes | `KnowledgeEvidenceSelectionRegressionTests`, `KnowledgeArtifactRegressionTests`, `KnowledgeEvidenceRegressionTests`, `KnowledgeRetrievalRegressionTests`, `HandlerDocumentaryEvidenceTests` | 88 |
| Preparación por lenguaje natural | `ViewSpecPreparationFormatTests` | 15 |
| Cliente de Copilot | `CopilotClientProviderTests` | 6 |
| Generación | `ViewScriptGenerationTests` | 168 |
| Creación (decisiones puras) | `ViewCreationTests` | 119 |

No existe ninguna prueba automática del conocimiento curado; se comprobó en vivo.

### Comprobación en vivo

API y Web propias en los puertos estándar (libres; Visual Studio no estaba en ejecución), con la creación apagada en la configuración local. Ambos procesos se detuvieron al terminar. El índice de conocimiento no se reconstruyó (archivo con fecha 2026-10-05, sin cambios).

| Caso | Resultado |
|---|---|
| Carga de la página | Sin «+ Nueva vista» ni pestañas; API, GestionEngine e identidad disponibles; sin errores de consola |
| Consulta de Wiki | «¿Para qué sirve el handler CustomLevelSort y cómo se configura?» → el chat pidió una aclaración |
| Seguimiento de conversación | «Cómo configurarla técnicamente» → respuesta que resuelve el tema de la pregunta anterior |
| Fuentes | Cuatro fuentes citadas (`Agrupadores SORT CustomLevelSort.msg`, `Cambio de estado entre estado manteniendo visualización.msg`, `Documento Handlers.docx`, `Handlers.xlsx`) y marca «Consultado en GestionEngine» |
| Preparación por lenguaje natural | «Crea una vista llamada vLimpiezaCheck01» → ViewSpec válido en «Vista en progreso» |
| Generación | «Generar script» → script de 2 737 caracteres con `Vistas`, `Estados`, `Filas` y `ConfiguracionesEstados`; «Script generado. No ejecutado.» |
| Creación | `POST api/view-script/create` → 403 `NotAllowed`, «La creación de vistas está deshabilitada en esta instancia». Nada se ejecutó |
| Errores del chat | Mensaje de 501 caracteres → 400; identificador de conversación inválido → 400; mensaje vacío → 400, cada uno con su texto |
| Conocimiento curado | `GET api/knowledge/curated` → 200 para pendientes y aprobados, ambos con 0 entradas; el «Laboratorio de conocimiento» abre y muestra «No hay entradas pendientes de revisión» |
| Endpoint retirado | `POST api/view-templates/grid-query` → 404 |
| Registro de la API | Sin errores |

### No validado

- **Conocimiento curado como fuente de una respuesta** y el ciclo enseñar → aprobar: no hay entradas y crear una habría sido una escritura de datos no pedida.
- **«Enseñar a DesignerIA» abierto desde un hueco de conocimiento:** la respuesta de prueba no devolvió huecos. Su código y su marcado no se modificaron.
- **Creación real:** sólo pruebas unitarias y el rechazo con la creación apagada. No se escribió en ninguna base.
- **Layout a menos de 1100 px:** sigue con la regla anterior que estrecha el panel central; no se cambió.
- Las respuestas de Copilot no son deterministas: la comprobación en vivo acredita que el recorrido funciona, no el texto exacto.

Observación: la página mostró 20 vistas registradas en la base local; el checkpoint de fase 5 anotaba 19. No se investigó ni se tocó.

## 5. Documentos

Anotados con una nota fechada, sin reescribir su contenido histórico: `DesignerIA-Roadmap.md`, `DesignerIA-Contexto.md`, `DesignerIA-Fase05-Checkpoint.md` y `DesignerIA-Arranque-VM.md`. Los de fases 1 a 4 se conservan como registro y citan archivos ya retirados.
