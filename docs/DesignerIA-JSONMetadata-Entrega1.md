# DesignerIA — Primera entrega: creación desde JSONMetadata

Fecha: 11 de octubre de 2026. Informe de implementación del paso T1. **Documento histórico:** el estado final, con la prueba real de `vDemoGrilla01` y la segunda entrega, está en `DesignerIA-JSONMetadata-Entrega2.md`. Desde entonces el documento produce el plan directamente, sin pasar por `ViewSpec`, y los controles de navegación y los varios estados dejaron de rechazarse. Contrato y reglas: `DesignerIA-JSONMetadata-Contrato.md`. Fichas: `capacidades/`. Ejemplos: `ejemplos/`.

## Estado

**Implementado y validado sin escritura en bases:** el recorrido adjuntar → preparar → revisar → confirmar → solicitar creación funciona en la Web y en la API.

**No declarado como creación funcional.** No se creó ninguna vista: la creación está apagada en la configuración local y este encargo no autorizó escribir en ninguna base. Falta comprobar la metadata escrita, la apertura y edición en Designer y el reconocimiento en Motor (sección «Reservas»).

Sin escrituras Git, sin SQL directo, sin dependencias nuevas, sin cambios de autenticación ni de configuración. No se repitió la limpieza. No se migraron las instrucciones de Copilot ni se retiró `ViewSpec`.

## Qué hace

1. **Botón «Crear vista»** en la barra lateral → diálogo que lee un archivo `.json` (máximo 256 KB).
2. **Preparar** envía el texto a `POST api/view-json/prepare`. La API hace la lectura estricta, completa las convenciones de la versión `1.0`, valida estructura y soporte y, si no falta nada, consulta el catálogo.
3. **Revisión:** el diálogo muestra qué falta o hay que corregir (ubicación, causa y dato necesario), de dónde sale cada dato (recibido, completado, derivado, catálogo) y la definición efectiva.
4. **Crear vista** sólo se habilita sin diagnósticos; pide confirmación y envía la definición efectiva a `POST api/view-json/create`.
5. **La API vuelve a leer, expandir y validar** antes de crear, y el catálogo se comprueba otra vez dentro del camino de creación. El resultado aparece en el panel «Creación de vista».

La API nunca recibe SQL del cliente, no interviene Copilot y no se genera, modifica ni ejecuta ningún SP de negocio.

## Alcance

| Se materializa | Se rechaza con su ubicación |
|---|---|
| Vista vacía `site`: sólo identificador y versión | Segundo estado, fila o área |
| Vista `go` con un estado, una fila, un área y una grilla | Controles, left menu, `controls` y `leftMenuItems` de un estado |
| Parámetros con valor fijo | Fuentes `control`, `leftMenu`, `pivot`, `variable`; MyVision |
| Estado `Pantalla`, área `Area01`, descripción = identificador, cuando se omiten y hay uno solo | `settings`, handlers, botones, gráfico, JSControl, JS |
| Fila `<estado>F0` y objeto `<área>_Obj` | Dimensiones, título, ayuda, pivot, pop-up, layout, seguridad |
| Configuración de grilla de referencia | `site` con contenido; `go` sin contenido |

Controles y left menu conservan su lugar en el contrato: se definen en la vista y los estados los referencian. Declararlos hoy produce un diagnóstico por elemento; no se omiten.

## Cómo se comparte la creación

```text
Archivo JSON → JsonMetadataReader → JsonMetadataExpander → JsonMetadataValidator → JsonMetadataViewSpecMapper ─┐
                                                                                                             ├→ ViewCreationService
ViewSpec (chat y api/view-script) ───────────────────────────────────────────────────────────────────────────┘     (preflight, plan, SQL, ejecución, verificación)
```

La definición efectiva se traduce a la entrada del camino de creación existente. Ese camino **no se modificó**, de modo que las rutas actuales conservan todo lo que aceptan, incluidos los nombres explícitos `Fila01` y `Grilla01`. La entrada por documento usa los nombres derivados. No hay una segunda canalización ni reglas de creación duplicadas.

Diferencia con el plan documentado antes: allí se proponía convertir cada `ViewSpec` al documento. Se invirtió el sentido para cumplir la compatibilidad pedida. Invertirlo de nuevo corresponde a T3, cuando `ViewSpec` deje de tener consumidores.

## Diff

Nuevos:

| Archivo | Líneas | Responsabilidad |
|---|---|---|
| `DesignerIA.Contracts/JsonMetadataDocument.cs` | 57 | Forma del documento `1.0` |
| `DesignerIA.Contracts/JsonMetadataDraft.cs` | 59 | Petición, borrador efectivo, anotación, diagnóstico |
| `DesignerIA.Api/Services/ViewDocuments/JsonMetadataReader.cs` | 223 | Lectura estricta |
| `…/JsonMetadataExpander.cs` | 217 | Convenciones de la versión |
| `…/JsonMetadataValidator.cs` | 376 | Estructura, referencias y soporte |
| `…/JsonMetadataViewSpecMapper.cs` | 54 | Nombres derivados y traducción al camino de creación |
| `…/JsonMetadataService.cs` | 177 | Preparar y crear; ubicar los resultados del catálogo |
| `…/JsonMetadataNode.cs` | 37 | Acceso al árbol JSON, rutas y diagnósticos |
| `DesignerIA.Api/Controllers/ViewDocumentController.cs` | 46 | `api/view-json/prepare` y `create` |
| `DesignerIA.Web/Components/JsonMetadataCreateDialog.razor` | 264 | Carga, revisión y confirmación |
| `DesignerIA.Api.Tests/JsonMetadataTests.cs` | 458 | 48 pruebas |
| `docs/ejemplos/*.json` | — | Vista vacía, grilla y grilla incompleta |

Editados, medidos contra la copia previa:

| Archivo | Cambio | Motivo |
|---|---|---|
| `DesignerIA.Web/Pages/Home.razor` | +17 | Botón, diálogo y traslado del resultado al panel existente |
| `DesignerIA.Web/_Imports.razor` | +1 | `@using DesignerIA.Web.Components` |
| `DesignerIA.Web/wwwroot/css/app.css` | +97 | Estilos del diálogo |
| `DesignerIA.Api/Program.cs` | +2 | Registro de `JsonMetadataService` |
| `DesignerIA.Contracts/ViewCreationPreflightResult.cs` | −1, +13 | Campo opcional `DeclaredParameters`: la firma leída del SP |
| `DesignerIA.Api/Services/ViewScripts/ViewCreationPreflightService.cs` | −1, +5 | Devolver esa firma |

Sin cambios: `ViewCreationService`, `ViewScriptGenerator`, `ViewCreationVerifier`, `ViewScriptGenerationValidator`, `ViewCreationPlan*`, `ViewScriptController`, `ViewSpec*`, `ChatResult`, `CopilotChatService`, conocimiento, conocimiento curado, configuración y todas las pruebas existentes.

Recuperación previa a la entrega: `artifacts/entrega-json-20261011/` (patch de todo lo pendiente, copia de los archivos editados y sus hashes). La de la limpieza sigue en `artifacts/limpieza-20261011/`.

## Ejemplos utilizables

| Archivo | Resultado al preparar |
|---|---|
| `ejemplos/vista-vacia.json` | Completo: `site`, estado `Pantalla` inicial sin filas |
| `ejemplos/grilla.json` | Completo en la base local: estado `Pantalla`, área `Area01`, fila `PantallaF0`, objeto `Area01_Obj`; alias y SP resueltos por el catálogo |
| `ejemplos/grilla-incompleta.json` | Seis diagnósticos: 2 por corregir, 3 por completar y 1 sin soporte. No pasa a vista vacía ni recibe alias o SP |

`grilla.json` depende del ambiente: necesita el alias `GestionEngine` y el SP `dbo.PRC_GET_GRID_TEST_SORT_COLUMN`.

## Comprobaciones realizadas

### Compilación y pruebas

| Comprobación | Resultado |
|---|---|
| `dotnet build DesignerIA.slnx` | 0 errores, 17 advertencias, las mismas de antes |
| `dotnet test` | **905 superadas, 0 con error.** Las 857 anteriores pasan sin ninguna modificación; 48 nuevas |

Qué fijan las 48 nuevas:

1. **Expansión exacta e idempotente** de la vista vacía y de la grilla, incluido el orden de las propiedades.
2. **Grilla incompleta:** los seis diagnósticos con su ruta, severidad y código; el documento sigue sin alias, con su SP tal como se escribió y con `go`.
3. **Rechazos:** catorce capacidades declaradas sin soporte, cada una en su ubicación; estados, filas y áreas de más, uno por uno; propiedades desconocidas, tipos incorrectos, versión ausente o no admitida; JSON inválido, propiedad repetida, coma final y raíz que no es un objeto.
4. **Ausente, `null`, `""` y `[]`** como cuatro casos distintos; parámetro sin valor, vacío y `null`.
5. **Varios estados:** no se deriva nombre ni estado inicial por posición.
6. **Compatibilidad:** el documento vacío produce **el mismo script** que la vista vacía heredada; la grilla usa nombres derivados por el mismo generador; un `ViewSpec` con `Fila01` y `Grilla01` sigue generando con esos nombres.
7. **Catálogo:** cada rechazo se ubica en su propiedad; los parámetros omitidos se convierten en un pendiente con la firma; las anotaciones no exponen IDs físicos.

### En vivo

API y Web propias en los puertos estándar, con la creación apagada. Detenidas al terminar. La aplicación hizo sus lecturas habituales contra la base local; no hubo escrituras. El índice de conocimiento no se reconstruyó.

| Caso | Resultado |
|---|---|
| Diálogo: `grilla.json` → Preparar | «Documento completo»; seis valores completados, tres derivados y cuatro de catálogo; SP encontrado `[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]`; firma `@QueryString (varchar)` con la nota de la excepción heredada |
| Diálogo: Crear vista → Confirmar | 403: «La creación de vistas está deshabilitada en esta instancia», mostrado en el panel «Creación de vista»; vistas registradas sin cambio (20) |
| Diálogo: `grilla-incompleta.json` | «2 por corregir, 3 por completar, 1 sin soporte»; «Crear vista» deshabilitado |
| API: vista vacía | Sin diagnósticos; volver a preparar la definición efectiva devuelve el mismo texto |
| API: JSON inválido | Un error `JsonInvalido`, sin documento efectivo |
| API: alias inexistente | Error en `…data.connection` |
| API: SP inexistente | Error en `…data.storedProcedure` |
| API: SP con parámetros sin declararlos | Pendiente en `…data.parameters`: «@ParamUno (varchar), @ParamDos (varchar)» |
| API: identificador existente | Error `NombreExistente` en `view.id` |
| API: crear una grilla sin datos | `Rejected`, con los dos pendientes; nada ejecutado |
| Ruta heredada: `ViewSpec` vacío | Genera las cuatro inserciones con `PantallaF0` |
| Ruta heredada: `ViewSpec` con `Fila01` y `Grilla01` | Genera con esos nombres |
| Chat: consulta de Wiki | Respuesta con cuatro fuentes y «Consultado en GestionEngine» |
| Chat: «Crea una vista llamada vChequeoT1» → Generar script | ViewSpec válido; «Script generado. No ejecutado.» |
| Registro de la API | Sin errores |

## Reservas

Lo que impide declarar creación funcional:

1. **Ninguna vista creada por esta entrada.** Hace falta habilitar la creación en un proceso propio y la autorización explícita para escribir en una base concreta.
2. **Metadata escrita sin comprobar** para los nombres nuevos (`Pantalla`, `PantallaF0`, `Area01_Obj`). El script de la vista vacía es idéntico al heredado y el de la grilla sale del mismo generador, pero ninguno se ejecutó con estos nombres.
3. **Designer:** sin comprobar que abre ambas vistas, que permite editarlas y qué cambia al guardarlas (nombres de estado, fila y objeto; alta de `LMFiltros`).
4. **Motor:** sin comprobar que reconoce la vista vacía ni que muestra la grilla con los nombres derivados. La única grilla vista en Motor usó `Estado01`, `Fila01` y `Grilla01`.
5. **`LMFiltros` en la vista vacía:** se preserva la forma heredada, sin esa fila; Designer sí la escribiría.
6. **Valor fijo de parámetros:** generado y probado unitariamente; no visto en Motor.
7. **Heredadas:** SQL Server 2008 R2; creación automática con Motor configurado y apertura por «Abrir vista».

Límites conocidos de la implementación:

- Un alias inexistente produce dos errores en la misma ubicación (no existe y no tiene conexión configurada): son dos comprobaciones del catálogo.
- Los rechazos del catálogo se ubican por el tema que devuelve el preflight; un tema nuevo caería en el documento en general, no en una propiedad.
- El texto de la excepción heredada del SP de prueba sigue en `ViewCreationPreflightService.FromParameters`, con su redacción anterior («plantilla»).
- La carga del archivo se probó asignándolo al control desde el navegador integrado, no con el selector del sistema operativo.
- Sin validar en vivo: conocimiento curado como fuente de una respuesta y «Enseñar a DesignerIA» desde un hueco de conocimiento. Su código no cambió.

## Siguiente

Sobre el mismo contrato: comprobación en Designer y Motor de esta entrega; después, controles con ítems y `cambiarEstado`, varios estados y fuentes de parámetro; Word/TXT y migración del lenguaje natural (T2); composición grande. Nada de eso está iniciado.
