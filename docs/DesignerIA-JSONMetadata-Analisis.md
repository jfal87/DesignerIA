# DesignerIA — Análisis de metadata para creación desde JSON

Fecha: 11 de octubre de 2026, tercera revisión (añade B.7 vista vacía, B.8 lo que emite el chat y B.9 composición grande). Contiene **lo investigado**: base de RAG, catálogo, matriz de trazabilidad y huecos. Lo que se propone representar y lo que se materializa en la primera entrega están en `DesignerIA-JSONMetadata-Contrato.md`; la limpieza aplicada, en `DesignerIA-JSONMetadata-Limpieza.md`; el resumen para retomar, en `DesignerIA-JSONMetadata-Checkpoint.md`.

Clasificación usada: **C** confirmado por lectura de código o artefacto, **I** inferido, **P** pendiente / falta evidencia. «Confirmado» significa leído en la fuente citada; no significa ejecutado en Designer ni en Motor.

## 0. Estado recontrastado

| Dato | Valor observado |
|---|---|
| Rama / HEAD | `feature/create-view-executor` / `28532c2` |
| Rama publicada | `feature/publish-phase5` (`9e9fe7a`): un único commit sobre `main` (`0d8f8bf`), con historia distinta de la rama local. Frente a `28532c2` difiere en 8 archivos: `ViewCreationTests.cs`, `appsettings.Development.example.json` y seis documentos; ningún archivo de `Services`, `Controllers`, `Contracts` ni `Web` |
| Árbol de trabajo al iniciar | 20 archivos modificados sin confirmar: fase 6.1 (parámetros con valor fijo) en Contracts, generador, preflight, verificador, validador, plantilla, Home, vista previa, CSS y dos archivos de pruebas; más cuatro documentos |
| SQL | No se ejecutó ninguna consulta directa: el encargo no autoriza servidor, base ni autenticación. En la validación de la limpieza, la propia aplicación hizo sus lecturas habituales contra la base local |

### Fuentes usadas

| Fuente | Ubicación | Versión / fecha |
|---|---|---|
| Designer (escritura y carga) | `KnowledgeSource/DesignerRealEvidence/Clases/EngineUpdater.cs`, `Scripts/Designer.js`, `Clases/ScriptBuilder.cs` | Idénticos byte a byte a los de `Designer.zip` (SHA-256 comparado); ZIP fechado 2025-09-22 |
| Designer (DTO y servicio) | `Designer.zip`: `Designer/Dev/Clases/DTO/*.cs`, `Servicios/WCFServices.svc.cs` | Lectura con `unzip -p`, sin extraer al repositorio |
| Motor | `Motor.zip`: `FrameWorkMetaData/Clases/Estaticas/SMetadata.cs`, `Herramientas.cs`, `EstructuraMetadata/Estado.cs`, `ObjetoDeArea.cs`, `FULL/Default.aspx.cs`, `FULL/Servicios/FrameWorkRender.asmx.cs`, `wsGetJsControl.asmx.cs`, `FULL/Scripts/PreCoreV2.js` | Sin fecha de versión en el ZIP; existen PreCore, V1, V2 y V3: se leyó V2, que corresponde a `StyleVersion=V2` de la URL acordada |
| Esquema y procedimientos | `KnowledgeSource/Imports/ScriptCreateGestionEngine.sql` | Script fechado 28/09/2026 |
| Export `vpruebaJFAL` | `KnowledgeSource/vpruebaJFAL.sql` | `VersionActual` 19.2632, `F_Update` 2026-08-28 |
| Export `vPreciosSemanales_CDA` | `KnowledgeSource/vPreciosSemanales_CDA.sql` | `VersionActual` 1.193, filas con fecha 2025-06-25 |
| Export `AppPrecios` | `KnowledgeSource/AppPrecios.sql` | `VersionActual` 41.230; sólo se usaron sus conteos y cabecera |

Los ZIP están fuera de `C:\Code\DesignerAI`. Se leyeron porque el encargo los declara autorizados; no se movieron ni se copiaron al repositorio (una copia temporal de `PreCoreV2.js` quedó en la carpeta temporal de la sesión, fuera del repositorio).

## A. Base de RAG y lo reutilizable

**Base del chat/RAG: `74ca306` («Cierra etapa de estabilización del chat y RAG»), confirmada por diferencia de archivos, no por fecha.**

- **C** — `git diff --name-status 74ca306 HEAD` no lista ningún archivo de `Services/Knowledge*`, `CuratedKnowledge`, `Conversation*`, `CopilotChatService`, `CopilotClientProvider`, `KnowledgeEvidencePolicy`, `PlannerFailurePolicy`, extractores, controllers de conocimiento, `MarkdownChatRenderer` ni sus pruebas. El árbol de trabajo tampoco los modifica.
- **C** — Por tanto **no hay mejoras de RAG posteriores a `74ca306` que preservar aparte**: el RAG actual es exactamente el de esa base. No procede ningún rollback.
- **C** — Lo posterior a `74ca306` es sólo creación: 28 archivos de código (+3376/−163) y la carpeta `docs/`.

**Límite de esta evidencia.** La comparación demuestra que los archivos de RAG no cambiaron. No demuestra por sí sola que el chat se comporte igual: el chat comparte piezas con la creación, y esas piezas sí cambiaron. La ausencia de regresiones se acredita con las pruebas y la comprobación en vivo descritas en `DesignerIA-JSONMetadata-Limpieza.md`.

### Dependencias compartidas entre el chat/RAG y la creación

| Pieza compartida | Qué comparte | Cambió desde `74ca306` | Riesgo para el RAG |
|---|---|---|---|
| `DesignerIA.Web/Pages/Home.razor` | Una sola página: conversación, fuentes, indicadores, «Actualizar conocimiento», «Laboratorio de conocimiento», «Enseñar a DesignerIA» y el panel «Vista en progreso» | Sí: las fases 3 a 6.1 añadieron formulario, pestañas, vista previa y creación | Alto: cualquier edición toca la página del chat. Tras la limpieza, la zona de navegación y conversación vuelve a coincidir con la de `74ca306` |
| `DesignerIA.Web/wwwroot/css/app.css` | Estilos del chat, de los modales de conocimiento (`.modal-*`) y de la creación | Sí | Medio: el modal del formulario usaba las mismas clases `.modal-*`; se conservan |
| `Contracts/ChatResult.cs` | `ViewSpecResult?` viaja dentro de la respuesta del chat | No | Alto si se cambia `ViewSpec`: forma parte del contrato del chat |
| `Contracts/ViewSpec*.cs` | Borrador del chat y entrada de la generación | Sí: `ViewType`, nombres, preset y parámetros, todos opcionales al final | Medio: los campos añadidos son anulables y no alteran lo que produce el chat |
| `ViewSpecKnowledgeGap` y el panel «Conocimiento faltante» | Única entrada a «Enseñar a DesignerIA» | No | Alto si se retira el panel: se pierde el acceso al conocimiento curado desde una vista |
| `ViewSpecNormalizer`, `ViewSpecValidator` | Los usan el chat (`CopilotChatService`, `ConversationTurnCompletion`) y la generación | Sí: `ViewType` y estados duplicados | Medio: una regla nueva de validación cambia la respuesta del chat |
| `ConnectionStrings:GestionEngineLocal` | Salud, metadata para Copilot, handlers, catálogo, preflight y creación | No | Cambiar la clave mueve todos los servicios a la vez |
| `Program.cs` | Registro de servicios y CORS (`https://localhost:7276`) | Sí: una línea (`ViewCreationService`) | Bajo |
| `appsettings.json` | `ConnectionAliases`, `Motor`, `ViewCreation` | Sí: tres secciones nuevas | Bajo: el chat no las lee |
| `HandlerExamplesService` | Lectura del catálogo de acciones de handler para el chat | No | Será la fuente natural para resolver acciones cuando el contrato admita handlers |

Piezas que ya existían en `74ca306` y mezclan chat y creación:

| Pieza | Situación en `74ca306` | Observación |
|---|---|---|
| `CopilotChatService` (intención `ViewCreation`, `ViewSpecInstructions`, `BuildViewSpecChatResult`) | Ya preparaba un ViewSpec desde lenguaje natural | Entrelazada con router, planner y pruebas de regresión del chat (`ConversationCapabilityRouterTests`, `PlannerContract*`, `ViewSpecPreparationFormatTests`) |
| `ViewSpecResponseParser`, `ViewSpecNormalizer`, `ViewSpecValidator` | Existían | Los usa el chat; el generador los reutiliza |
| Panel «Vista en progreso» y «Generar script» en `Home.razor` | Existían; «Crear vista» estaba deshabilitado | El panel también aloja «Conocimiento faltante → Enseñar a DesignerIA», que es RAG |
| `ViewScriptController` (`generate`), `ViewCreationPreflightService`, `ViewCreationPlan*`, `ViewScriptGenerator`, `ViewScriptGenerationValidator`, `SqlLiteralEscaper`, `StringConnectionCatalogService` | Existían en versión mínima (vista vacía `site`) | Ampliados en fases 3–6.1 |

### Qué genera hoy el materializador (recontrastado)

**C** — `ViewScriptGenerationValidator.Validate` rechaza toda forma distinta de estas dos, sin SQL parcial:

1. `site`, un estado que es el inicial, cero filas lógicas.
2. `go`, un estado que es el inicial, exactamente una fila con exactamente un área de tipo `Grilla`, preset `grid-consulta-v1`, sin handlers, con parámetros de valor fijo opcionales.

**C** — `ViewCreationPlan` tiene un único `State`, una única `Row` y un `Grid` opcional: la colección `ViewSpec.States` no se recorre al generar (`ViewCreationPlanFactory.Create` toma `First` y `FirstOrDefault`). Que el DTO admita varios estados no implica que se generen.

**C** — Parámetros (fase 6.1, sin confirmar en Git): sólo valor fijo, sin fuente (`Variable` vacía); tipos `varchar`, `nvarchar`, `char`, `nchar`, `int`, `bigint`, `smallint`, `tinyint`, `bit`; cobertura exacta de los parámetros de entrada del SP; se rechazan `logon_user` y `gruposvistausuario`, apóstrofe, comillas, `\`, `<`, `>`, `-mas-`, `-andpersand-` y no ASCII.

**C** — El verificador (`ViewCreationVerifier`) compara conteos y valores contra ese mismo plan de una fila; no es genérico.

## B. Recorrido Designer → metadata → Motor

### B.1 Cómo guarda Designer

- **C** — El navegador envía todo el modelo de edición como JSON (`Designer.js`, `saveView` → `WCF.SetVista(JSON.stringify(designer.view))`); `EngineUpdater.SaveView` → `View.Create`.
- **C** — `View.Create` arma **un script** en una transacción `[TR1]`: guarda favoritos en temporales, ejecuta `eliminarVista '<Id>'`, declara contadores con `ISNULL(MAX(Id),1)` por tabla (`GetIndexesQry`), inserta `Vistas` y recorre estados (`CreateStates`). **Guardar es borrar y reinsertar**: todos los IDs físicos cambian en cada guardado.
- **C** — Los valores se concatenan sin escapar comillas (`'" + valor + "'`). Un apóstrofe en un texto rompe el script de Designer. DesignerIA no debe heredar esto: usa su escape central.
- **C** — `new JavaScriptSerializer().Deserialize(json.Replace("null", "\"\""))`: Designer reemplaza **toda** aparición del texto `null` en el JSON de la vista. Por eso el JS se guarda con `null` → `\u0000` (ver B.6).
- **C** — Carga: `WCFServices.GetVista` → `VistaDTO.RecoveryData` lee tabla por tabla con `GetSelectQuery` y `Designer.js` (`loadView`, `getAreas`, `setProperties`, `setGridProperties`, `setChartProperties`) reconstruye el modelo evaluando los JSON de configuración.

### B.2 Cómo lee Motor

- **C** — Motor no lee tablas directamente: usa procedimientos `recuperar*` (`SMetadata.cs`). Sus `JOIN` son las claves reales:

| Procedimiento | Clave de unión relevante | Consecuencia para la creación |
|---|---|---|
| `recuperarEstadoDefault` | `Estados.[Default] = 1` y `IdVista` | Exactamente un estado default por vista |
| `recuperarIdsEstados` | `IdVista`, orden por `IdEstado` | El orden de estados es el de inserción |
| `recuperarEstructuraEstado` | `IdEstado` numérico **o** `Estados.Descripcion` | El nombre del estado es una clave funcional (URL, `cambiarEstado`) |
| `recuperarDatosObjetoDeArea` | `Areas.Nombre = @NombreArea` dentro del estado; `MapeoParametros` por `IdEstado + NombreArea + Parametro = ParametrosSP.Nombre` (**sin** `IdStoredProcedure`) | Nombre de área único por estado; nombre de parámetro único por área |
| `recuperarConfiguracionObjetoDeArea`, `recuperarHandlersObjetoDeArea` | `Areas.Nombre` dentro del estado | Igual |
| `recuperarDatosWebControls` | `WebControls.IdEstado`; mapeos con `NombreArea = 'divWebControls'` + `IdStoredProcedure` + nombre de parámetro | `divWebControls` es un nombre de área reservado |
| `recuperarDatosLeftMenu` / `recuperarContenidoLeftMenu` | `LeftMenus.IdEstado`; ítem por `ItemsLeftMenu.Variable`; mapeos con `NombreArea = 'LeftMenu'` | `leftMenu` es un nombre de área reservado; la variable del ítem es su clave |
| `recuperarMapeoParametros` | Todos los del estado salvo `NombreArea = 'LeftMenu'` | Es la lista que Motor envía al navegador |

- **C** — `SMetadata.agregarEstado`: el tipo de objeto sale de `TipoObjetosDeArea.Tipo` en minúsculas; `Altura`/`Ancho` de área en 0 se convierten en 100; el área lee de la configuración del objeto `Hashtag`, `UnidadAnchoArea`, `UnidadAltoArea`, `MarginLeft`, `MarginTop`, `UnidadAltoFila`, `VisualizarCampos`.
- **C** — `Default.aspx.cs` pasa a minúsculas el IdVista de la URL (`limpiarCache`, `IdVista`).

### B.3 Catálogo contrastado

| Elemento observado | En Designer (`getStructure`) | Id físico que escribe `EngineUpdater` | Tipo que lee Designer/Motor | Estado |
|---|---|---|---|---|
| WC Combo box | `combo`, subtipo simple / expansible / checkbox | `IdTipoWebControl` 1 / 6 / 8 | `Combo`, `ComboBook`, `ComboCheck` | **C** ids; **I** correspondencia id↔nombre |
| WC Calendar | `calendar`, diario / mensual | 2 / 7 | `Calendar`, `CalendarMes` | **C** / **I** |
| WC Button List | `buttonList` (también `button`) | 4 | `ButtonList`, `Button` | **C** / **I** |
| WC Button Clean | `buttonClean` | 15 | `Button Clean` | **C** / **I** |
| WC Checkbox List | `checkBoxList` | 11 | `CheckBox` | **C** / **I** |
| WC Radio Button List | `radioButtonList` | 12 | `Option` | **C** / **I** |
| WC Text | `textBox` | 9 | `TextBox` | **C** / **I** |
| WC Pivot | `pivot`, subcontrol de un área | 13 | `Pivot` | **C** / **I** |
| Subcontrol TituloArea / BotonAyuda | Propiedad con `bindingta` del objeto | 14 / 16 | `TituloArea`, `BotonAyuda` | **C** |
| Área: Objeto de Área | `area` + `objetoDeArea` | `Areas` + `ObjetosDeArea` | — | **C** |
| Área: Menú Lateral | Fila con `Modo = Panel Lateral` | `Filas.Ancho = 1` | `setObject`: `Ancho == 1` → `Tipo = Lateral` | **C** |
| Objeto Grid | `grid` | `IdTipoObjetoDeArea` 2 | `Grilla` | **C** |
| Objeto Chart | `chart` | 4 | `GraficoHtml5` | **C** |
| Objeto JSControl | `jsControl` | 5 | `JSControl` | **C** |
| Gantt, HeatMap | Definidos en `getStructure`; `ContenidosArea` ofrece `heatMap` y excluye `gantt` | `GetAreaObjectDBKey` y `GetAreaObjectPropertiesKey` sólo conocen grid, chart y jscontrol; para otro tipo, `CreateAreaObject` falla dentro de un `catch` vacío y no inserta el objeto | — | **C**: Designer actual no los persiste; fuera de alcance |
| Left Menu (ítem) | `sideMenu`, tipos Simple 3, Buscador 1, Buscador Múltiple 2, Sel. múltiple 4, Sel. múltiple chk 5, Buscador Múltiple chk 6, Rangos 7 | `IdTipoLeftMenu` = ese número; `IdTipoInstanciaLeftMenu` = 1 | `TiposLeftMenu.Tipo` | **C** ids; **P** nombres del catálogo |
| Left Menu (solapa) | `sideMenuTab` | `SolapasLeftMenu`, `Orden` = 1 siempre | — | **C** |

**P** — La correspondencia exacta id ↔ `Tipo` de `TiposWebControl`, `TipoObjetosDeArea`, `TiposLeftMenu`, `TiposHandlers` y `TiposEjecucionesSP` está en tablas de catálogo. El esquema las define, pero ningún archivo autorizado trae sus filas. Se confirma con un `SELECT` de solo lectura cuando se autorice.

### B.4 Matriz de trazabilidad

Abreviaturas: EU = `EngineUpdater.cs`; DJ = `Designer.js`; SM = `SMetadata.cs`; SQL = `ScriptCreateGestionEngine.sql`.

| Capacidad / propiedad | Ámbito | Obligatorio / default | Dónde se guarda y relaciones | Designer que escribe | Motor que lee / usa | Regla para JSON | Soporte |
|---|---|---|---|---|---|---|---|
| IdVista / Nombre | Vista | Obligatorio; ambos iguales | `Vistas.IdVista` = `Vistas.Nombre` (nvarchar 250) | EU `GetViewInsertQry` (`this.ID` dos veces) | `recuperarEstadoDefault`; URL en minúsculas | `view.id`; único en el ambiente | **C** |
| Descripción | Vista | Obligatoria en tabla (350) | `Vistas.Descripcion` | EU `GetViewInsertQry` | `recuperarEstadoDefault` la devuelve | `view.description`; si falta, el id | **C** |
| Tipo de vista | Vista | Designer propone `site`; valores `go`, `site` | `Vistas.TipoVista` | EU, DJ `reset` | **P** efecto en Motor no auditado | `view.type` obligatorio en el JSON | **C** escritura |
| Seguridad | Vista | `false` | `Vistas.UsaSeguridad` 0/1 | EU; el export conserva la del destino | `recuperarEstadoDefault`; `SSeguridad.cs` no auditado | `view.usesSecurity`; v1 sólo `false` | **C** escritura, **P** efecto |
| Versión inicial | Vista | `1.0` | `Vistas.VersionActual` y `Vistas.VistaVersionado` = `'1.0'` | EU (`isnew ? "1.0"`) | Devuelve `VistaVersionado` | La asigna el backend; no va en el JSON | **C** |
| Historial de versiones | Vista | Sin fila al crear | `VistaVersionado` sólo al guardar una vista existente o al exportar | EU `scriptVistaVersionado`, `ViewVersion.Create` | No interviene en el render | Fuera de alcance; no insertar | **C** |
| Auditoría y fijos | Vista | `IdOwner` 1, `IdHistoricoCambio` 1, `ArchivoJs` `''`, `Frente` `''`, `IdStringsConnectionMyVision` NULL | `Vistas` | EU | — | Los asigna el backend | **C** |
| JS de la vista | Vista | Vacío | `Vistas.JsCode`, codificado | DJ `save`: `"`→`\u0022`, `'`→`\u0027`, `null`→`\u0000` | `Default.aspx.cs` `agregarCodigoJavascript` revierte los tres | `view.js` texto plano; el backend codifica | **C** |
| Estado | Estado | Al menos uno | `Estados`: `Descripcion` = nombre, `[Default]`, `ArchivoJs` `''`, `LeftMenuFijo` 0 | EU `CreateStates` | `recuperarEstructuraEstado` por id o nombre | `states[].name`; único en la vista | **C** |
| Estado inicial | Estado | Exactamente uno | `Estados.[Default] = 1` | EU (`esDefault`) | `recuperarEstadoDefault` | `isDefault` en un solo estado | **C** |
| Configuración de estado | Estado | Siempre una fila | `ConfiguracionesEstados.Configuracion` = `{"filters","leftMenus","isPopUp","popUp","idLayout"}` | EU `CreateStateConfig` | `Default.aspx.cs` la publica como `State_<nombre>Settings` | Se deriva de `controls`, `leftMenuItems`, `popUp`, `layout` | **C** |
| Pop-up | Estado | `false`; tamaño 1200×700 | `isPopUp`, `popUp:{Ancho,Alto}` | EU, DJ `popUp` | **P** render | `popUp: {width,height}` o nulo | **C** escritura |
| Layout | Estado | `00` | `idLayout`: `00`, `01`, `02`, `99` | DJ `constants.Layouts` | **P** efecto | `layout` | **C** escritura |
| Fila | Fila | — | `Filas.Nombre` = `<estado>F<índice>`; `Ancho` = modo (0 fila, 1 panel lateral); `Altura` = entero sin unidad | EU `CreateRows` | SM `agregarEstado` agrupa por `NombreFila` | Sin nombre en el JSON: lo deriva el backend; `mode`, `height` | **C** |
| Área: nombre | Área | Si falta, el id generado `area_NNNN` | `Areas.Nombre` (uso en parámetros nvarchar 75), saneado a `[0-9a-zA-Z_]` | EU `CreateAreas` + `ParseID` | Clave de todos los `recuperar*` de área; id del DOM | `areas[].name` obligatorio, `[A-Za-z0-9_]`, único por estado | **C** |
| Área: dimensiones | Área | `auto` | `Areas.Ancho/Altura` (entero; 0 si no numérico) + `UnidadAnchoArea/UnidadAltoArea` (`%`, `px` o `calc(...)`) en la configuración del objeto | EU `CreateAreas`, `CreateAreaObject` | SM: 0 → 100; lee unidades | `width`, `height` como texto (`auto`, `50%`, `300px`, `calc(...)`) | **C** |
| Área: márgenes y alto de fila | Área | Vacíos; `rowHeight` `817` | `MarginLeft`, `MarginTop`, `UnidadAltoFila` en la configuración del objeto | EU; DJ `getStructure("area")` | SM los lee | `marginLeft`, `marginTop`, `rowHeight` | **C**; **P** efecto visual de `817` |
| Área: botones | Área | `Expandir`, `Excel Sin Formato` | `BotonesArea:{BotonN:{ID,ToolTip}}` o `""` | EU `GetAreaConfig` | **P** | `buttons[]`; catálogo: Help, Expandir, Compartir, Comentarios, Excel Sin Formato, Excel Con Formato, Excel Con Template | **C** escritura |
| Área: Hashtag | Área | `""` | `Hashtag` | EU `GetAreaConfig` | SM | `hashtag` | **C** |
| Área: `position`, `enabled` | Área | — | **No se persisten** | Definidos en DJ, ignorados por EU | — | No van en el JSON | **C** |
| Objeto de área | Objeto | Uno por área | `ObjetosDeArea`: `Nombre` = `<área>_Obj`, `IdTipoObjetoDeArea`, `IdExtraccionDato` (o −1), `UsaPaginador` 0 | EU `CreateAreaObject` | `recuperarDatosObjetoDeArea` (`NombreObjeto`) | Sin nombre en el JSON: `<área>_Obj` | **C** |
| Extracción y SP | Objeto / WC / LM | Opcional | `ExtraccionesDatos.Descripcion` = SP; `StoredProcedure`: `Nombre`, `Descripcion` `''`, `OrdenEjecucion` 0, `IdStringConnection`, `IdTipoEjecucionSP` 1 | EU `DataExtraction` | SM: conexión = `StringConnection + "_" + Ambiente`; `[usebox]` en el nombre activa caja | `data.connection` (alias) + `data.storedProcedure` | **C** |
| Parámetro del SP | Extracción | Designer declara todos los del SP | `ParametrosSP`: `Nombre` sin `@`, `UsaComillas` por tipo, `EsMyVision`, `TipoParametro` | EU `DataExtraction`, `validaTipoParametro` | `Herramientas.recuperarComandoQuery` | Tipo y comillas los resuelve el catálogo del SP | **C** |
| Mapeo de parámetro | Estado + área | Una fila por parámetro | `MapeoParametros`: `IdEstado`, `NombreArea`, `Parametro`, `Variable` (fuente o `''`), `ValorDefault`, `IdStoredProcedure`, `VariableRequest` `''` | EU `CreateMappings` | `recuperarMapeoParametros` → lista `Parametros` al navegador | `parameters[].source` + `default` | **C** |
| Configuración de grilla | Objeto | Defaults de `getStructure("grid")` | `ConfiguracionesObjetoDeArea.Configuracion`: JSON plano de cadenas + configuración del área | EU `GetGridConfig` (`GridProperties`) | SM `SetearConfiguacion`; `[]` → `<br>` o título dinámico | `settings` con claves físicas conocidas | **C** |
| Configuración de gráfico | Objeto | Defaults de `getStructure("chart")` | Mismo campo; `EjeX/EjeY/EjeY2` objetos; `SerieN` objetos | EU `GetChartConfig` | **P** render | `settings`, `axes`, `series[]` | **C** escritura |
| Configuración de JSControl | Objeto | `{}` + área | Sólo `TituloArea`, `BotonAyuda` y configuración de área | EU `GetJSControlConfig` | `PreCoreV2.js` rama `jscontrol`; `wsGetJsControl` | Representable; render por JS | **C** escritura, **P** contrato de render |
| Botones de grilla | Objeto | Ninguno | `BotonN:{Clase,ToolTip}` dentro de la configuración | EU `GetGridConfig` | **P** | `gridButtons[]` | **C** escritura |
| Handler de grilla | Objeto grid | Ninguno | `Handlers`: `IdAccionHandler`, `IdTipoHandler` (del catálogo `AccionesHandler`), `Parametros` (texto JSON), `IdsAplicarAccion` `'All'`, `orden` | EU `CreateHandlers` | `recuperarHandlersObjetoDeArea`; SM agrupa por tipo y acción | `handlers[]` por nombre de acción, resuelto en el catálogo | **C** escritura, **P** catálogo |
| Web control | Vista (se inserta en estados default o pop-up) | — | `WebControls`: `IdTipoWebControl`, `IdEstado`, `IdExtraccionDato` o −1, `Nombre`, `Titulo`, `ValoresComodin`, `Orden` | EU `CreateFilters` | `recuperarDatosWebControls`; agrupa por `NombreObjeto` | `controls[]` a nivel vista | **C** |
| Ítems de control | WC | Opcional | `ItemsWebControl`: `ItemId`, `ItemDescripcion`, `ItemDefault` | EU `CreateFilters` | SM: si hay ítems, **no** se ejecuta el SP | `items[]` | **C** |
| Acciones y visibilidad | WC | — | `ConfiguracionesFiltros.Configuracion`: `{"actions":{...},"SufijoBoton","visibleEn","autoHide","enabled"}` | EU `CreateFilters` | `Default.aspx.cs` → `<nombre>Settings`; `PreCoreV2.js` `executeCommonActions` | `actions[]`, `visibleIn`, `autoHide`, `enabled` | **C** |
| Filtros activos del estado | Estado | — | `filters` = nombres separados por coma | EU `CreateStateConfig` | `State_<nombre>Settings` | `states[].controls[]` | **C** |
| Left menu | Estado default o pop-up | Siempre una fila `LMFiltros` en esos estados | `LeftMenus` → `SolapasLeftMenu` → `ItemsLeftMenu` (`Nombre` = título, `Variable`, `IdTipoLeftMenu`, `Orden`, `ValorDefault`) | EU `CreateLeftMenu`, `CreateSideMenuTabs`, `CreateLeftMenuItems` | `recuperarDatosLeftMenu`, `recuperarContenidoLeftMenu` | `leftMenu.tabs[].items[]` | **C** |
| LM activos del estado | Estado | — | `leftMenus` = `Solapa:Título:Variable` | EU `CreateStateConfig` | **I** mismo uso que `filters` | `states[].leftMenuItems[]` | **C** escritura |
| Pivot | Área | — | WC tipo 13 en el estado del área + `"Pivot":"<nombre>"` en la configuración | EU `CreateAreaObject`, `CreateFilters` | **P** | `areas[].pivot` | **C** escritura |
| Título / ayuda dinámicos | Área | `"[]"` si vacío | WC tipo 14 / 16 con SP propio; `TituloArea` = `[<IdArea>]`, `BotonAyuda` = `[<área>_Help]` | EU `CreateAreaObject` | SM `recuperarTituloArea`, `recuperarAreaConfig` | `title`, `help` con `data` opcional | **C** escritura |
| Favoritos, URL cortas, traducciones, comentarios | Uso | — | `FavoritosEngineNCA`, `Url_Vistas`, … | El guardado sólo los conserva | Alteran el valor en tiempo de ejecución | Fuera del JSON | **C** |

### B.5 Casos delicados

**1. Opciones configuradas frente a opciones por SP.**
- **C** — Un WC puede tener `items` (texto, valor) y/o un `binding` a SP. Designer guarda ambos si están presentes (`CreateFilters`).
- **C** — Motor: si el control tiene ítems, usa los ítems y no ejecuta el SP (`recuperarWebControlsConDAL`: `recuperarItems().Count > 0` → `ListItems`; si no, `DataSet`).
- **C** — `ItemDefault` = 1 para el ítem cuyo valor coincide con `ValoresComodin` (o con lo que sigue a `default:`), o el primero si `ValoresComodin` está vacío.
- **C** — Left menu: los ítems siempre obtienen su contenido de un SP (`recuperarContenidoLeftMenu` exige `ExtraccionesDatos` y `StoredProcedure` con `INNER JOIN`). No existe una lista fija de opciones de LM.
- Regla JSON: `items` y `data` son excluyentes en un control; declarar ambos es un diagnóstico.

**2. Parámetros del SP que alimenta un control frente a parámetros de una grilla que reciben su selección.**
- **C** — Son filas distintas de `MapeoParametros` con `NombreArea` distinto: `divWebControls` (SP del control), `leftMenu` (SP del ítem de LM) y el nombre del área (SP del objeto).
- **C** — `vPreciosSemanales_CDA`: `leftMenu | MESDESDE | wcMesDesde` (el LM se alimenta de un WC) y `AreaGrillaPrincipal | MESDESDE | wcMesDesde` (la grilla recibe el mismo WC). 100 mapeos: 78 en áreas con fuente, 2 fijos, 13 `leftMenu`, 7 `divWebControls`.
- **C** — Los mapeos de un WC se insertan en el estado donde se inserta el WC (default o pop-up), no en cada estado.

**3. Orígenes de valor.**
- **C** — Designer ofrece: filtro del estado, left menu del estado, pivot del área, «Usar Variable» (texto libre), además de «P/Defecto» y la marca MyVision (`loadBindingPopUp`).
- **C** — Todos se guardan igual: `Variable` = nombre saneado de la fuente (`ParseID(GetItemName(...))`), `ValorDefault` = default. **La metadata no distingue el tipo de fuente.**
- **C** — Fuentes observadas en `vPreciosSemanales_CDA`: controles (`wcInforme`, `wcPeriodo`…), variables de LM (`lmCalidad`, `ICliente`, `lf_pedido`), variables libres (`varFamilia`, `varSegmento`, `varFila`) y nombres sintetizados por Motor (`AreaGrillaClienteRenglones`, `AreaGrillaClienteNroPagina`).
- **C** — Navegador: `retornarValorObjetoMotor(nombre, default)` devuelve el default si el nombre está vacío; si no, busca `window[nombre]`, un elemento con ese id o sus variantes `_ValDef` y `_sb`. Una variable libre existe cuando el JS de la vista llama a `objetoParametro(nombre, valor)`, que crea un `input` oculto.
- **C** — Motor resuelve por sí mismo `logon_user` y `gruposvistausuario` (por nombre de parámetro) y los parámetros MyVision.
- Regla JSON: `source.kind` ∈ `fixed`, `control`, `leftMenu`, `pivot`, `variable`. El tipo sirve para validar la referencia; se materializa como nombre.

**4. Ausente, vacío, default y NULL; selección única y múltiple.**
- **C** — Parámetro **no declarado** en `ParametrosSP`: Motor no lo envía y rige el default del propio SP. Es el caso de `@QueryString` en la grilla de `vpruebaJFAL`.
- **C** — Declarado con valor vacío y comillas: se envía `''` (o `ValorQuery` si existe). Sin comillas: se envía `0` (`recuperarComandoQuery`).
- **C** — Declarado sin fila de mapeo: el `LEFT JOIN` da vacío y se comporta igual que el caso anterior.
- **C** — NULL: un valor fijo no puede expresarlo. Sólo aparece cuando un parámetro de petición vale `null` en la variante con `requestParams` (títulos y configuración de área).
- **C** — Motor elimina apóstrofes del valor con comillas (`Replace("'", "")`) y traduce `-mas-` ↔ `+` y `-andpersand-` ↔ `&`.
- **C** — Selección múltiple en defaults reales: `*||100||101||1`, `Reducción||SIN`, `*` como «todos». **P** — el formato exacto que emite cada tipo de control en ejecución (`||`, `;`, `[;]`) está en `TerniumWebControls/.../Core.js`, no auditado.
- **I** — `ValoresComodin` de calendarios guarda expresiones como `Hoy Menos 1 meses` que resuelve `LibreriasValoresComodin/LibreriaComodin.cs`. **P** — gramática completa.

**5. Normalización, ámbitos y unicidad.**
- **C** — `ParseID` elimina todo carácter fuera de `[0-9a-zA-Z_]` en nombres de área, WC, variable de LM y fuente. No se aplica al nombre del estado ni a títulos.
- **C** — Nombre de fila y de objeto no los elige el usuario: `<estado>F<índice>` y `<área>_Obj`. La V1 de DesignerIA escribió `Fila01` y `Grilla01`: Motor la mostró (evidencia manual del usuario), pero un guardado posterior desde Designer los renombraría. El JSON no debe exponerlos.
- **C** — Unicidad exigida por las uniones: área por estado; parámetro por área; control por estado (Motor agrupa filas consecutivas por nombre); variable de LM por estado.
- **I** — Controles, variables de LM, variables libres y áreas comparten el espacio de nombres del DOM y de `window` (`<nombre>Settings`, `document.getElementById`). Conviene exigir unicidad en toda la vista. Es una regla de DesignerIA, no una restricción de la base.
- **C** — `divWebControls` y `leftMenu` no pueden usarse como nombre de área.

**6. Dependencias, orden de carga, eventos y navegación.**
- **C** — Orden de inserción de Designer por estado: estado → filas → áreas → (extracción, SP, parámetros, mapeos, objeto, WC de título/ayuda, handlers, configuración) → left menu → controles → configuración de estado.
- **C** — Acciones de control: `RefrescarAreas`, `cambiarEstado`, `RefrescarWC`, `RefrescarLM`, con parámetros separados por coma; se guardan como `{"actions":{"cambiarEstado":["Pantalla8823"]}}` y `executeCommonActions` las ejecuta con `eval`.
- **C** — Navegación entre estados sin JS: botón con `cambiarEstado` y el nombre del estado destino (`vpruebaJFAL`: `wcGrilla` ↔ `wcGrafico`).
- **C** — Navegación a detalle en `vPreciosSemanales_CDA`: funciones del JS de la vista (`clickEnCeldaGrilla`, `clickEnWC`, `clickEnItemLM`) que fijan variables con `objetoParametro` y cambian de estado. **P** — qué handler de grilla habilita el clic.
- **P** — Orden de carga entre controles dependientes (un LM alimentado por un WC) en el navegador.

**7. JSControl frente al JS de la vista.**
- **C** — JS de la vista: texto único en `Vistas.JsCode`; Motor lo inyecta en un `<script>`; expone ganchos por nombre (`iniciarFuncionesPropiasDeLaVista`, `personalizarObjetoDeArea`, `personalizarJsonGrafico`, `clickEnWC`, …).
- **C** — JSControl: objeto de área tipo 5, con extracción y SP; su configuración sólo guarda título, ayuda y datos del área. No contiene código.
- **I** — El render de un JSControl lo hace JS (de la vista o global) con los datos que entrega `wsGetJsControl`. **P** — contrato exacto entre datos y función de render.
- Comportamiento que **ya es metadata**: acciones de control, visibilidad por ambiente, auto-ocultar, handlers de grilla, botones, pop-up, layout. Comportamiento que **requiere código**: variables libres, navegación a detalle por clic en celda, personalización de gráficos, render de JSControl.

**8. Petición, favoritos y defaults.**
- **C** — `Estado.RecuperarJsonParametroNombreObjeto` sustituye el default por el valor del favorito o de la petición si coincide el nombre de la fuente o, en su defecto, el del parámetro (en minúsculas).
- **C** — `objetoParametro` toma el valor del favorito cuando la vista tiene uno.
- **C** — `VariableRequest` se lee (`ObjetoRequest`) pero Designer siempre escribe `''`. **P** — uso real.
- Regla: el JSON describe el valor configurado; favoritos y petición no forman parte de la creación.

**9. Columnas de resultado y contrato del renderer.**
- **C** — La metadata no guarda la lista de columnas de una grilla. Guarda índices y nombres dentro de la configuración (`IdColumnaCodigo`, `IdColumnaDescripcion`, `AnchoColumnas`, `FixedColumnsMobile`) y dentro de los parámetros de handlers (`{"Columnas":"4:#FFCD80"}`, `{"ocultar":"Orden"}`).
- **I** — Las columnas vienen del resultado del SP. **P** — la forma que exige cada renderer (`GridHTML5Renderer`, etc.), que es responsabilidad del SP de negocio.
- **C** — Gráfico: `Series[].Columna` es la clave de cada serie; con `GeneracionAutomatica` las series salen del resultado.

### B.6 Reglas de persistencia que la creación debe respetar

| Regla | Evidencia | Estado |
|---|---|---|
| Contadores: `ISNULL(MAX(Id),1)` + 1 por inserción, dentro de la transacción | EU `GetIndexesQry` | **C**; DesignerIA añade bloqueos `UPDLOCK, HOLDLOCK` |
| `IdExtraccionDato = -1` cuando no hay SP | EU `CreateAreaObject`, `CreateFilters`; exports | **C** |
| Fila `LMFiltros` en todo estado default o pop-up, aunque no haya ítems | EU `CreateLeftMenu` | **C** |
| `"TituloArea":"[]"` y `"BotonAyuda":"[]"` cuando están vacíos (grilla, JSControl) | EU `GetGridConfig`; export | **C**; en gráficos el export muestra `""` |
| Configuración de objeto: valores como cadenas; booleanos en minúsculas | EU `GetGridConfig` | **C** |
| JS: `"`→`\u0022`, `'`→`\u0027`, `null`→`\u0000` | DJ `save`; `Default.aspx.cs` | **C** |
| `IdTipoEjecucionSP` = 1 | EU; exports | **C** |
| Conexión: `StoredProcedure.IdStringConnection`; Motor necesita `<alias>_<Ambiente>` en su propia configuración | SM `agregarObjetoDeArea` | **C**; comprobable sólo en el ambiente de Motor |
| Fechas de auditoría: Designer usa la fecha sin hora y `User_Update` NULL | EU | **C**; DesignerIA usa `GETDATE()` |

### B.7 Vista nueva y vista vacía (tercera revisión)

Evidencia para la convención «un documento con sólo el identificador es una vista vacía».

| Hecho | Evidencia | Estado |
|---|---|---|
| Designer, vista nueva: `TipoVista` propuesto `site`, `UsaSeguridad` false, `VersionActual` `1.0`, `ArchivoJs` y `Frente` vacíos | `Designer.js` `reset` (`TipoVista … selectedIndex: 1`) | **C** |
| Designer, vista nueva: un estado default cuyo nombre es `Pantalla` **más cuatro dígitos al azar** | `Designer.js` `new` → `addVisualization("Pantalla", { isDefault: true })`; `addVisualization` añade `getRandomID()` salvo `randomize === false`; exports: `Pantalla8668`, `Pantalla2275` | **C** |
| Designer, estado nuevo: `Filas: []`, sin filtros ni left menu, `esPopUp` false, `idLayout` `00` | `Designer.js` `addVisualization` | **C** |
| Designer, estado sin filas: no inserta ninguna fila en `Filas` | `EngineUpdater.CreateRows` recorre `Filas` | **C** |
| Designer, estado default: inserta siempre `LeftMenus` `LMFiltros` y una `ConfiguracionesEstados` | `EngineUpdater.CreateLeftMenu`, `CreateStateConfig` | **C** |
| Configuración de un estado sin nada: `{"filters":"","leftMenus":"","isPopUp":false,"popUp":{},"idLayout":"00"}` | `EngineUpdater.CreateStateConfig` con listas vacías | **C** |
| Motor sólo encuentra un estado si tiene al menos una fila | `recuperarEstructuraEstado`: `inner join dbo.filas`; `SMetadata.agregarEstado`: sin filas, `estructuraEncontrada` queda en false y el estado se guarda como nulo | **C** |
| Vista vacía heredada de DesignerIA: `site`, estado `Pantalla` sin sufijo, una fila física `PantallaF0` con ancho y alto 0, la configuración vacía anterior, **sin** fila `LMFiltros` | `ViewSpecNormalizer.DefaultStateName`, `ViewCreationPlanFactory`, `ViewScriptGenerator`; `DesignerIA-Fase02-Contrato.md` sección 7; script generado el 11 de octubre en la validación de la limpieza | **C** |
| Que Designer o Motor reconozcan esa vista vacía | No hay evidencia archivada. El commit base dice «creación mínima validada» sin precisar qué se validó, y en `74ca306` el botón de crear estaba deshabilitado | **P** |

Lectura:

- `Pantalla` sin sufijo y la fila física `PantallaF0` son **convenciones de DesignerIA**, no valores de Designer. La primera hace determinista el nombre; la segunda es coherente con la unión de Motor, que exige una fila.
- La ausencia de `LMFiltros` en la vista vacía es una diferencia conocida con Designer. La grilla de DesignerIA sí la inserta.
- Ningún valor de estas convenciones procede de una vista de negocio.

### B.8 Lo que emite hoy el chat

- **C** — El esquema que `CopilotChatService.ViewSpecInstructions` pide a Copilot sólo tiene `name`, `description`, `initialState`, estados, filas y áreas con `type`, `storedProcedure`, `connectionAlias` y `handlers`. No incluye `viewType`, nombres de fila, área u objeto, preset ni parámetros.
- **C** — Por tanto, lo único que el chat prepara y el generador acepta es la vista vacía `site`. Una grilla preparada por el chat llega sin `viewType`, y el validador la rechaza como forma no soportada.
- **C** — La forma `go` con grilla sólo se alcanza hoy enviando un `ViewSpec` completo a `api/view-script/generate` o `create`. Tras la limpieza, ninguna pantalla lo hace; la cubren las pruebas.
- **C** — La deserialización de `ViewSpec` ignora propiedades desconocidas (opciones web de `System.Text.Json`).

### B.9 Composición grande: fragmento de análisis

**No es un ejemplo válido del contrato.** No hay export de `sic_budget_costo_variable` en la evidencia autorizada; se usa `vPreciosSemanales_CDA.sql` (versión 1.193) para revelar relaciones y huecos. Los nombres son los del export.

| Relación observada | Datos del export | Qué exige del contrato | Hueco |
|---|---|---|---|
| 5 estados, uno default | `Pantalla2275` (default), `Pantalla4432`, `Pantalla2553`, `Pantalla7000`, `Pantalla4626` | Varios estados y un default | — |
| Una fila y un área por estado | 4 grillas y 1 gráfico; áreas `AreaGrillaPrincipal`, `area_5904`, `AreaGrillaDetalle`, `AreaGrillaCliente`, `AreaGraficoDetalle` | Nombre de área elegido o generado | `area_5904` es un id generado por Designer |
| 19 controles, todos en el estado default | Combos (tipo 1), calendarios (2 y 7), listas de botones (4), casillas (11), opciones (12) | Controles a nivel de vista | Correspondencia id ↔ tipo: catálogo sin leer |
| Controles activos por estado | `filters` distinto en cada estado; `wcVolver` sólo en los de detalle | `states[].controls` | — |
| Control con ítems fijos y control con SP | `wcInforme` sin extracción y con ítems; `wcMesDesde` con extracción | `items` o `data`, excluyentes | Los 33 ítems no se transcribieron |
| Comodín de fecha | `Hoy Menos 1 meses`, `Hoy Menos 2 meses` | `wildcard` | Gramática sin auditar |
| Un left menu, una solapa, 10 ítems | Solapa `Filtros`; variables `ICliente`, `lmCalidad`, `lmFamilia`, `lf_pedido`…; tipos 2 y 5 | `leftMenu.tabs[].items[]` | Nombre de los tipos 2 y 5: catálogo sin leer |
| Default de selección múltiple | `*`, `*||1`, `*||100||101||1` | `wildcard` como texto | Formato por tipo de control |
| Un control alimenta a un ítem de left menu | `leftMenu · MESDESDE · wcMesDesde` | `source.kind = control` dentro de `data` de un ítem | Orden de carga |
| La misma fuente alimenta varias áreas | `AreaGrillaPrincipal · MESDESDE · wcMesDesde`, `AreaGrillaDetalle · MESDESDE · wcMesDesde` | Mapeo por estado y área | — |
| Variables libres | `varFamilia`, `varSegmento`, `varGerencia`, `varFila`… (27 mapeos) | `source.kind = variable` | Existen sólo si el JS de la vista llama a `objetoParametro` |
| Fuentes sintetizadas por Motor | `AreaGrillaClienteRenglones`, `AreaGrillaClienteNroPagina` | Sin decidir | Origen de esos nombres sin auditar |
| 100 parámetros, 2 con valor fijo | Sólo `AreaGrillaDetalle` tiene dos fijos | — | La primera entrega cubriría 2 de 100 |
| 13 handlers | Acciones 2, 3, 4, 503, 526, 535, 577, 624; parámetros como `{"ocultar":"Orden"}`, `{"Columnas":"4:#FFCD80"}` | `handlers[]` por nombre de acción | El export sólo trae ids; los parámetros nombran columnas del resultado del SP |
| Navegación | `wcVolver` con `cambiarEstado`; detalle por `clickEnCeldaGrilla`, `clickEnWC`, `clickEnItemLM` | `actions[]` y `view.js` | Qué handler habilita el clic en celda |
| JS de la vista | Más de veinte funciones | `view.js` como texto | DesignerIA no lo interpreta ni lo genera |
| Conexión | `IdStringConnection` 6 en los 19 SP | Alias | El alias de 6 no consta en el export |

Lectura: la vista real depende sobre todo de controles, left menu, fuentes de parámetro y JS. Ninguno entra en la primera entrega. Un documento que los declare se rechazaría con una línea por elemento.

## Huecos registrados

1. **Sin export de `sic_budget_costo_variable`.** Sólo aparece en correos indexados de la Wiki (variantes `_preview`, `_preview2`, `_preview_CDA`, con handler `CustomLevelSort`). La composición grande se contrastó con `vPreciosSemanales_CDA`.
2. **Filas de los catálogos** (`TiposWebControl`, `TipoObjetosDeArea`, `TiposLeftMenu`, `TiposHandlers`, `AccionesHandler`, `TiposEjecucionesSP`, `StringsConnection`): no hay archivo; requiere lectura SQL autorizada.
3. **Formato del valor en selección múltiple** por tipo de control y gramática de `ValoresComodin`.
4. **Contrato de render de JSControl** y del resultado esperado por cada renderer de grilla y gráfico.
5. **Efecto en Motor** de `TipoVista`, `UsaSeguridad`, `idLayout`, pop-up, botones de área y `VariableRequest`.
6. **Tabs.** Designer no tiene un elemento de pestañas de composición: sólo solapas de left menu y la propiedad de texto `Tabs` del gráfico. `TerniumTabList` existe en Motor pero no se alcanza desde la metadata de Designer.
7. **Versión efectiva de PreCore** en el ambiente; se leyó `PreCoreV2.js`.
8. **La demo estática** citada en el encargo no está dentro de `C:\Code\DesignerAI` (búsqueda de `*.html`, `*demo*`, `*mock*`). No se tocó nada relacionado.
9. **SQL Server 2008 R2** sigue sin comprobarse; `sys.dm_exec_describe_first_result_set_for_object`, que usa el preflight, existe desde SQL Server 2012. Afecta sólo a la lectura de preflight, no al script generado.
10. **Catálogo de acciones de handler.** Ningún archivo lo trae, pero la aplicación ya lo lee: en la validación de la limpieza, el chat devolvió `CustomLevelSort` con `IdAccionHandler` 611 e `IdTipoHandler` 1. Es un dato observado en la base local, no un catálogo completo.
