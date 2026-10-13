# DesignerIA — JSONMetadata compacto, expansión explícita y transición

Fecha: 11 de octubre de 2026, quinta revisión. **Están implementadas la primera entrega (T1) y la segunda (varios estados y web controls de navegación)**, con las decisiones de la sección 11. Resultados de la prueba real, cambios, comprobaciones y reservas: `DesignerIA-JSONMetadata-Entrega2.md` (estado final) y `DesignerIA-JSONMetadata-Entrega1.md` (primera entrega). La vista `vDemoGrilla01` se creó y su metadata se comprobó en la base local; **su apertura en Designer y su reconocimiento en Motor siguen sin comprobarse**, porque ninguno de los dos está desplegado en este equipo.

Documentos relacionados: `DesignerIA-JSONMetadata-Analisis.md` (lo investigado, incluida la composición grande como prueba de relaciones y huecos), `docs/capacidades/` (una ficha breve por capacidad) y `DesignerIA-JSONMetadata-Limpieza.md` (limpieza aplicada).

## 1. Un contrato, dos estados del mismo documento

- **Un solo contrato.** El usuario escribe un documento breve; el backend lo **expande** a la definición efectiva, que es un documento del mismo contrato con las convenciones ya escritas. No hay DSL, ni claves abreviadas, ni sintaxis equivalentes, ni un segundo modelo de creación.
- **La compacidad viene de omitir lo derivable.** Las claves son legibles y las colecciones se conservan (`states`, `rows`, `areas`, `parameters`) para que el contrato crezca sin cambiar de forma.
- **Versión explícita.** `schemaVersion` es obligatoria. Los defaults pertenecen a la versión: `1.0` significa siempre lo mismo.
- **El borrador efectivo no autoriza nada.** Muestra qué se recibió, qué se completó y qué falta. La API valida de nuevo al crear.
- **La carga no usa IA.** Chat y RAG quedan fuera del camino obligatorio.

## 2. Ausente, `null`, cadena vacía y colección vacía

No se unifican. Cada uno tiene un significado único en todo el contrato.

| Forma | Significado | Tratamiento |
|---|---|---|
| Propiedad **ausente** | «Aplica la convención de esta versión» | Si existe convención, se completa y se anota su origen. Si no existe, es un **pendiente** |
| `null` | «No lo sé» | Siempre **pendiente** en esa ubicación, aunque exista convención. Nunca se rellena ni se interpreta como NULL de SQL |
| Cadena vacía `""` | Un valor explícito vacío | **Error** en identificadores, alias, SP y descripción. En el valor de un parámetro, ver abajo |
| Colección vacía `[]` | «Declaro que no hay elementos» | Depende de la colección; ver la tabla de la sección 3 |
| Propiedad desconocida | — | **Error.** No se descarta |

`null` como «no lo sé» sirve sobre todo a las propuestas de Copilot: lo que el usuario mencionó sin precisar queda señalado en su sitio y no se confunde con «usa el default».

### Parámetros: tres situaciones distintas

| Situación | Documento | Qué ocurre en Motor | Tratamiento |
|---|---|---|---|
| No declarar parámetros | `parameters` ausente o `[]` | No se envía ningún parámetro; rige el default de cada parámetro del SP | Se admite si el SP no declara parámetros de entrada. Si los declara, es un **pendiente** que enumera la firma leída del catálogo. Excepción heredada: decisión D4 |
| Declarar un parámetro con valor vacío | `"default": ""` | Motor enviaría `''` si usa comillas y `0` si no | **Error** en `1.0`: es una restricción de DesignerIA que ya aplica el validador; el efecto en Motor no está comprobado |
| Pretender NULL de SQL | `"default": null` | Un valor fijo no puede producir NULL; Motor sólo lo envía cuando llega `null` por la petición en títulos y configuración de área | **Pendiente:** `null` significa «no lo sé». El mensaje aclara que NULL de SQL no es representable |

Evidencia: `Herramientas.recuperarComandoQuery`; `ViewScriptGenerationValidator.DescribeParameterValueProblem`.

## 3. Tabla de omisiones y resolución

Origen del dato: **U** usuario · **D** default aprobado o nombre derivado por código · **C** catálogo autorizado del ambiente · **IA** propuesta de Copilot, que entra como si la hubiera escrito el usuario y pasa por las mismas validaciones · **—** nadie puede resolverlo sin más información.

| Propiedad y ámbito | Qué significa omitirla | Default o derivación | Evidencia | Responsable | Si no se resuelve |
|---|---|---|---|---|---|
| `schemaVersion` | No se puede omitir | U | Decisión de formato | Lectura estricta | Error: versión ausente o no admitida |
| `view.id` | No se puede omitir | U | `Vistas.IdVista` = `Vistas.Nombre` (`EngineUpdater.GetViewInsertQry`) | Validación; unicidad: catálogo | Pendiente si falta. Fuera de `[A-Za-z0-9_]` → error. Si ya existe en el ambiente, la creación se rechaza: el id no recupera ni clona nada |
| `view.description` | Usar el id | D: igual a `view.id` | `ViewSpecNormalizer` (herencia); `Vistas.Descripcion` es NOT NULL | Expansión | `""` → error; `null` → pendiente |
| `view.type` | Tipo por defecto | D: `site` | `Designer.js` `reset` (`selectedIndex: 1`); herencia de DesignerIA | Expansión | Con grilla, `site` es **no soportado**: la única combinación comprobada es `go`. Dato necesario: `"type": "go"` |
| `view.usesSecurity` | Sin seguridad | D: `false` | `Designer.js` `reset`; `UsaSeguridad` 0 | Expansión | `true` → no soportado |
| `view.js` | La vista no tiene JS | Ausencia es el valor (`JsCode` vacío) | `Designer.js` guardado; `EngineUpdater`; Motor `agregarCodigoJavascript` | Plan | Texto opcional; se codifica en `Vistas.JsCode`, se escapa como literal SQL y se verifica al crear. `null` → pendiente; tipos distintos de texto → error. Ver `docs/capacidades/javascript.md` |
| `states` | Vista vacía | D: un estado `Pantalla`, default, sin filas | Herencia de DesignerIA (`ViewSpecNormalizer.DefaultStateName`). Designer usa `Pantalla` + cuatro dígitos al azar | Expansión | `[]` → error: omitir la propiedad o declarar un estado |
| `states[].name` | Nombre por convención | D: `Pantalla` **sólo si la vista tiene un único estado** | La misma convención anterior, extendida (decisión D2) | Expansión | Con varios estados, cada estado sin nombre es un error. Duplicado → error |
| `states[].isDefault` | Estado inicial por regla | D: `true` **sólo si hay un único estado** | `recuperarEstadoDefault` exige uno | Expansión y validación | Con varios estados: debe haber exactamente uno en `true`; ninguno o más de uno → error. No se elige el primero |
| `controls` (vista) | La vista no tiene web controls | Ausencia es el valor | Designer guarda cada control una sola vez (`EngineUpdater.CreateWebControls`) | Validación | `[]` explícito significa lo mismo |
| `controls[].name` | No se puede omitir | U | `WebControls.Nombre`; es la clave que usan `filters` y el navegador | Validación | Pendiente. Fuera de `[A-Za-z0-9_]`, duplicado o igual al nombre de un área → error |
| `controls[].type` | No se puede omitir; **sin default** | U el nombre lógico; C el `IdTipoWebControl` | Catálogo `TiposWebControl` leído en la base local el 11 de octubre de 2026: `ButtonList` = 4 | Validación; el script comprueba el Id contra el catálogo antes de escribir | Pendiente. `buttonList` es el único soportado; otro tipo conocido → no soportado; desconocido → error |
| `controls[].items` | No se puede omitir | U: `text` y `value` de cada opción | `ItemsWebControl`; con opciones fijas Motor no ejecuta ningún SP | Validación | Pendiente si falta; `[]` → error |
| `controls[].actions` | No se puede omitir | U | `ConfiguracionesFiltros.Configuracion`, clave `actions` | Validación | Pendiente. Sólo una acción `cambiarEstado` con un parámetro: el nombre de un estado de la vista. Estado inexistente → error de referencia; otra acción conocida → no soportado |
| Orden, título, comodín, opción predeterminada, visibilidad y habilitación de un control | No son propiedades soportadas | D por versión: orden = posición en `controls`; título y comodín vacíos; primera opción predeterminada; `visibleEn` `A`; `enabled` `true`; `SufijoBoton` `false` | `EngineUpdater.CreateWebControls`, `CreateWebControlItems`; `Designer.js` | Plan | Declararlos (`title`, `order`, `wildcard`, `visibleIn`, `enabled`, `mode`, `autoHide`, `data`) → no soportado |
| Estado físico de un control | No es propiedad | D: el estado inicial | `EngineUpdater`: los controles se insertan en el estado inicial o pop-up | Plan | La excepción del pop-up no se materializa: `popUp` → no soportado |
| `states[].controls` | Ningún control activo en ese estado | Ausencia es el valor (`filters` vacío) | `ConfiguracionesEstados.Configuracion`, clave `filters`: nombres separados por coma | Validación; plan | Nombre que no está en `controls` de la vista → error de referencia; repetido → error. El orden escrito es el de definición en la vista |
| `states[].rows` | Estado sin contenido | D: `[]`. `[]` explícito significa lo mismo | Herencia; Designer crea el estado con `Filas: []` | Expansión | — |
| Fila física de un estado sin filas | No es propiedad | D: una fila `<estado>F0`, ancho y alto 0 | Herencia; `recuperarEstructuraEstado` une con `Filas` por `INNER JOIN` | Plan | — |
| Nombre de fila | No es propiedad | D: `<estado>F<índice>` | `EngineUpdater.CreateRows`; tres exports | Plan | — |
| `rows[].areas` | No se puede omitir | U | `ViewSpecValidator`: una fila sin áreas es un error | Validación | Ausente o `[]` → error |
| `areas[].name` | Nombre por convención | D: `Area01` **sólo si la vista tiene una única área** | Convención acordada para V1 (`DesignerIA-Contexto.md`, sección 5). Designer genera `area_NNNN` al azar | Expansión | Con varias áreas, cada área sin nombre es un pendiente. Explícito con caracteres fuera de `[A-Za-z0-9_]`, duplicado o reservado → error |
| `areas[].object` | No se puede omitir | U | Designer crea un objeto por área | Validación | Error |
| `object.type` | No se puede omitir; **sin default** | U | `GetAreaObjectDBKey` cae en grilla para tipos desconocidos: es un valor de respaldo del código, no una regla | Validación | Pendiente: tipo de objeto |
| Nombre de objeto | No es propiedad | D: `<área>_Obj` | `EngineUpdater.CreateAreaObject`; tres exports | Plan | — |
| `object.data` (grilla) | No se puede omitir | U | Designer admite un objeto sin SP (`IdExtraccionDato` −1), pero una grilla sin datos no tiene caso comprobado | Validación | **Pendiente:** SP y alias. Nunca se convierte en vista vacía |
| `data.connection` | No se puede omitir; **sin default** | U el alias; C el `IdStringConnection` | `StringsConnection.StringConnection`; el formulario retirado proponía `GestionEngine`, que era un valor de ejemplo | Validación; `StringConnectionCatalogService` | Pendiente si falta. Inexistente, ambiguo u Oracle → error de catálogo |
| `data.storedProcedure` | No se puede omitir | U como `esquema.nombre`; C la forma canónica `[esquema].[nombre]` | `sys.procedures` + `sys.schemas` | Validación; preflight | Pendiente si falta. Sin esquema → error. No visible → error de catálogo. Nunca se inventa |
| `data.parameters` | No declarar parámetros | D: `[]` | Sección 2 | Expansión; preflight | Pendiente si el SP declara parámetros de entrada |
| `parameters[].name` | No se puede omitir | U; C contrasta contra la firma | `sys.parameters` | Validación; preflight | Pendiente. No existe en el SP → error de catálogo |
| `parameters[].source` | Valor fijo | D: `{ "kind": "fixed" }` | `MapeoParametros.Variable` vacía; `retornarValorObjetoMotor` devuelve el default cuando no hay fuente | Expansión | Otro `kind` → no soportado |
| `parameters[].default` | No se puede omitir en valor fijo | U | Sección 2 | Validación | Ausente o `null` → pendiente; `""` → error |
| `parameters[].myVision` | No usa MyVision | Ausencia es el valor (`EsMyVision` 0) | `ParametrosSP.EsMyVision` | Lectura | Declarado → no soportado |
| Tipo, comillas y orden de cada parámetro | No son propiedades | C | `sys.parameters`; `EngineUpdater.validaTipoParametro` | Preflight | Tipo no admitido → error de catálogo |
| `object.settings` | Configuración de referencia | D por versión: la de `GridConfigurationPresets` | Sección 6 | Plan | Declarado → no soportado |
| Configuración de estado, `LMFiltros` | No son propiedades | D por regla | `EngineUpdater.CreateStateConfig`, `CreateLeftMenu`; herencia | Plan | — |
| Ids físicos, versión `1.0`, auditoría | No son propiedades | Asignados al crear | `EngineUpdater.GetIndexesQry`, `GetViewInsertQry` | Generador y ejecución | — |
| `leftMenu`, `leftMenuItems`, `handlers`, `pivot`, `title`, `help`, dimensiones, pop-up, layout, gráfico, JSControl | Ausencia es el valor | — | Fichas de `docs/capacidades/` | Validación de soporte | Declarados → no soportado, uno por elemento, con su ruta |

Lo que **nadie puede resolver** sin información adicional: el id de la vista, el SP, el alias, el valor de cada parámetro; con varios estados, sus nombres, cuál es el inicial y el nombre de cada área; y de cada control, su nombre, tipo, opciones y estado de destino. Ninguno se rellena.

## 4. Recorrido

```text
lectura estricta → expansión de convenciones → estructura, referencias y soporte
               → catálogo autorizado → borrador efectivo + diagnósticos → revisión → creación
```

| Paso | Qué hace | Responsable |
|---|---|---|
| 1. Lectura estricta | JSON válido, `schemaVersion` admitida, tipos correctos, sin propiedades desconocidas, tamaño máximo (256 KB). Conserva la diferencia entre ausente y `null` | API |
| 2. Expansión | Completa sólo las propiedades ausentes que tienen convención en esa versión. No toca un valor explícito. La definición efectiva resultante tiene el mismo tamaño máximo | API, función pura |
| 3. Estructura, referencias y soporte | Reglas de nombres, unicidad, un único estado inicial, datos requeridos, y lista explícita de lo soportado | API, función pura |
| 4. Catálogo | Nombre disponible, alias, conexión configurada, SP, firma de parámetros y valores. Sólo para extracciones completas | API: preflight, solo lectura |
| 5. Borrador efectivo | Documento expandido, anotaciones de origen y diagnósticos | API → Web |
| 6. Revisión | El usuario ve lo interpretado y sus pendientes | Web |
| 7. Creación | Repite los pasos 1 a 4 sobre el documento recibido, construye el plan, ejecuta y verifica | API |

Propiedades de la expansión:

- **Determinística:** la misma entrada y la misma versión producen el mismo documento. No depende del ambiente, de la hora ni de Copilot.
- **Idempotente:** expandir un documento ya expandido no lo cambia. Por eso la creación puede recibir el documento efectivo que el usuario revisó y volver a pasarlo por el mismo código.
- **Conservadora:** un dato explícito nunca se sustituye por un default. Las únicas normalizaciones son las documentadas: `parameters` ausente pasa a `[]`, `rows` ausente pasa a `[]`, y el SP se muestra como lo escribió el usuario (la forma canónica es un dato de catálogo, no una reescritura del documento).
- **Fijada por versión:** las convenciones de `1.0` son constantes del código asociadas a esa versión. Cambiar una exige una versión nueva del formato. Una prueba por ejemplo fija su expansión exacta, de modo que una actualización del programa no pueda alterar en silencio el significado de un documento guardado.
- **Una sola fuente para el plan:** el plan físico se construye desde la definición efectiva. Las condiciones del ambiente se revalidan al crear.
- **Un solo límite de tamaño:** el documento recibido y su definición efectiva comparten el mismo máximo explícito (`JsonMetadataReader.MaxContentLength`, 256 KB; el diálogo aplica el mismo valor al archivo). Un documento compacto puede caber y su definición efectiva no, porque la expansión añade propiedades y sangrado: en ese caso la preparación devuelve el error `DocumentoDemasiadoGrande` en la raíz y `canCreate` es falso. Así, **todo borrador habilitado para crear puede volver a recibirse** al confirmar. Una prueba fija el caso.

### Borrador efectivo

Respuesta compartida en Contracts, sin modelo paralelo:

| Parte | Contenido |
|---|---|
| `viewId` | Identificador de la vista, si el documento lo trae |
| `effectiveDocument` | El documento efectivo, del mismo contrato, como texto JSON. Es lo que se envía al crear |
| `annotations[]` | `path`, `origin` (`recibido`, `default`, `derivado`, `catálogo`) y una nota. Las convenciones físicas que no son propiedades (nombre de fila, de objeto, configuración) se anotan en la ruta de su dueño |
| `diagnostics[]` | Ver abajo |
| `canCreate` | Lo decide el backend: verdadero sólo sin diagnósticos, incluidos los del catálogo |

### Diagnósticos

| Campo | Contenido |
|---|---|
| `path` | Ubicación en el documento, por ejemplo `states[0].rows[0].areas[0].object.data.connection` |
| `severity` | `error`, `pendiente` o `noSoportado` |
| `code` | Identificador estable |
| `cause` | Por qué, en español |
| `needed` | El dato o la corrección necesarios |

| Severidad | Significa | Ejemplo |
|---|---|---|
| `error` | El documento está mal: hay que corregirlo | Propiedad desconocida, nombre duplicado, alias inexistente |
| `pendiente` | Falta un dato que alguien debe aportar | Grilla sin alias; parámetro sin valor |
| `noSoportado` | Es válido en el contrato, pero esta versión no lo materializa | `settings`, left menu, un control que no sea lista de botones, segunda fila o área |

Cualquiera de las tres impide crear. Copilot puede proponer correcciones, pero no decide si un documento es creable.

## 5. Ejemplos

### A. Vista vacía compacta

```json
{
  "schemaVersion": "1.0",
  "view": { "id": "vNueva" }
}
```

Definición efectiva:

```json
{
  "schemaVersion": "1.0",
  "view": { "id": "vNueva", "description": "vNueva", "type": "site", "usesSecurity": false },
  "states": [ { "name": "Pantalla", "isDefault": true, "rows": [] } ]
}
```

| Ruta | Origen | Valor o nota |
|---|---|---|
| `schemaVersion`, `view.id` | recibido | — |
| `view.description` | default | Igual al id |
| `view.type` | default | `site` |
| `view.usesSecurity` | default | `false` |
| `states` | derivado | Convención de vista vacía |
| `states[0]` | derivado | `Estados.Descripcion` = `Pantalla`, `[Default]` = 1; configuración `{"filters":"","leftMenus":"","isPopUp":false,"popUp":{},"idLayout":"00"}` |
| `states[0].rows` | derivado | Sin filas lógicas; una fila física `PantallaF0`, ancho y alto 0 |
| `view` | derivado | `VersionActual` y `VistaVersionado` = `1.0`; sin `LMFiltros` (decisión D3) |
| `view.id` | catálogo | Disponibilidad del nombre: se comprueba en el ambiente al preparar y al crear |

Estas son exactamente las cuatro inserciones de la vista vacía heredada (`Vistas`, `Estados`, `Filas`, `ConfiguracionesEstados`). **Su reconocimiento en Designer y en Motor no está acreditado**, y que el chat ya la genere no demuestra que la carga por archivo esté implementada.

### B. Grilla compacta completa

```json
{
  "schemaVersion": "1.0",
  "view": { "id": "vDemoGrilla01", "type": "go" },
  "states": [
    { "rows": [ { "areas": [ {
        "object": {
          "type": "grid",
          "data": { "connection": "GestionEngine", "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN" }
        }
    } ] } ] }
  ]
}
```

Definición efectiva:

```json
{
  "schemaVersion": "1.0",
  "view": { "id": "vDemoGrilla01", "description": "vDemoGrilla01", "type": "go", "usesSecurity": false },
  "states": [
    {
      "name": "Pantalla",
      "isDefault": true,
      "rows": [ { "areas": [ {
          "name": "Area01",
          "object": {
            "type": "grid",
            "data": {
              "connection": "GestionEngine",
              "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN",
              "parameters": []
            }
          }
      } ] } ]
    }
  ]
}
```

| Ruta | Origen | Valor o nota |
|---|---|---|
| `view.id`, `view.type`, `object.type`, `data.connection`, `data.storedProcedure` | recibido | — |
| `view.description`, `view.usesSecurity` | default | Igual al id; `false` |
| `states[0].name`, `states[0].isDefault` | default | `Pantalla`; `true` por haber un único estado |
| `states[0].rows[0]` | derivado | `Filas.Nombre` = `PantallaF0`, ancho y alto 0 |
| `areas[0].name` | default | `Area01`; ancho y alto 0 |
| `areas[0].object` | derivado | `ObjetosDeArea.Nombre` = `Area01_Obj`, tipo 2, sin paginador; configuración de referencia de la versión `1.0` |
| `data.parameters` | default | `[]`: no declarar parámetros |
| `states[0]` | derivado | Configuración de estado vacía y fila `LMFiltros` |
| `data.connection` | catálogo | El alias debe resolver a una única conexión de SQL Server; su `IdStringConnection` es propio de cada ambiente y no forma parte del documento |
| `data.storedProcedure` | catálogo | Forma canónica `[esquema].[nombre]` leída del catálogo; es la que se escribe en `ExtraccionesDatos` y `StoredProcedure` |
| `data.parameters` | catálogo | Firma leída al preparar. `DesignerIA-Contexto.md` (sección 6) documenta para este SP un único parámetro `@QueryString varchar(500) = ''`. No se declara: Motor no lo envía y rige su default. Es la excepción heredada de la decisión D4 |

Es la misma vista que `vDesignerIATest01`, con tres nombres distintos: estado `Pantalla` en vez de `Estado01`, fila `PantallaF0` en vez de `Fila01` y objeto `Area01_Obj` en vez de `Grilla01`.

### C. Grilla incompleta

```json
{
  "schemaVersion": "1.0",
  "view": { "id": "vDemoGrilla02", "type": "go" },
  "states": [
    { "rows": [ { "areas": [ {
        "titulo": "Ventas",
        "object": {
          "type": "grid",
          "settings": { "Renglones": 100 },
          "data": {
            "storedProcedure": "PRC_VENTAS",
            "parameters": [ { "name": "Anio" }, { "name": "Planta", "default": null } ]
          }
        }
    } ] } ] }
  ]
}
```

No se expande a una vista vacía ni recibe un SP o un alias. Diagnósticos:

| # | `path` | `severity` | `code` | Causa | Dato necesario |
|---|---|---|---|---|---|
| 1 | `states[0].rows[0].areas[0].titulo` | error | `PropiedadDesconocida` | `titulo` no existe en el contrato `1.0` | Quitarla; el título de área es `title` y todavía no está soportado |
| 2 | `…areas[0].object.settings` | noSoportado | `ConfiguracionNoAdmitida` | La versión `1.0` crea la grilla con la configuración de referencia | Quitar `settings` |
| 3 | `…object.data.connection` | pendiente | `DatoRequerido` | La grilla no indica alias de conexión | Un alias registrado en `StringsConnection` |
| 4 | `…object.data.storedProcedure` | error | `FormaNoAdmitida` | `PRC_VENTAS` no incluye esquema | `esquema.nombre` |
| 5 | `…data.parameters[0].default` | pendiente | `DatoRequerido` | El parámetro `Anio` no tiene valor | Un valor de texto |
| 6 | `…data.parameters[1].default` | pendiente | `ValorDesconocido` | `null` significa «no lo sé»; no representa NULL de SQL | Un valor de texto |

El catálogo no se consulta para esta extracción mientras le falten alias o SP válidos. `canCreate` es falso.

## 6. Configuración y nombres

**Configuración de la primera grilla.** Se propone la configuración de referencia ya probada, sin `settings`. Difiere de la que Designer da a una grilla nueva en cinco claves:

| Clave | Designer, grilla nueva | Referencia |
|---|---|---|
| `ColumnTotalsRenderMode` | `RENDER_TIT` | `RENDER_BK_TIT` |
| `AnchoColumnas` | `0` | `150` |
| `TitulosFijos` | `false` | `true` |
| `MostrarCeros` | `false` | `true` |
| `IndizarPorColumna` | `false` | `true` |

Ninguna afecta al caso mínimo: la referencia es la única configuración que Motor mostró en una vista creada por DesignerIA. Elegir defaults generales corresponde al paso en que se abra `settings`. La lista completa de claves, tipos y valores está en `docs/capacidades/grilla.md`.

**Nombres derivados.** Fila `<estado>F<índice>` y objeto `<área>_Obj`, como en Designer. Su aceptación por Motor y su estabilidad tras un guardado en Designer se comprueban al final (sección 10).

**Efecto sobre las entradas heredadas.** `ViewSpec` tiene `Row.Name` y `Area.ObjectName`. El chat no los emite, pero una llamada directa a las rutas existentes sí puede traerlos. No se ignoran: ver la sección 7.

## 7. Transición corregida

### Las dos formas heredadas

| Forma | Quién la produce hoy | Estado |
|---|---|---|
| Vista vacía `site` | El chat (lenguaje natural) y cualquier llamada con un `ViewSpec` sin filas | Activa en la pantalla |
| `go` con un estado, una fila y una grilla | Sólo una llamada directa a `api/view-script/generate` o `create` con un `ViewSpec` completo | Sin pantalla tras la limpieza; cubierta por pruebas |

La revisión anterior limitaba la entrada nueva a `go` con grilla y dejaba sin destino la vista vacía que el chat ya genera. Corrección: **la canalización de creación admite las dos formas**, y las dos entradas la comparten.

### Un solo camino de creación (como quedó implementado)

```text
Archivo JSON → lectura estricta → expansión → validación → catálogo → JsonMetadataPlanFactory ─┐
                                                                                             ├→ ViewCreationPlan → SQL → ejecución → verificación
ViewSpec (chat y api/view-script) → validación → catálogo → ViewCreationPlanFactory ─────────┘
```

La propuesta anterior convertía cada `ViewSpec` al documento. Eso habría obligado a rechazar los nombres explícitos `Fila01` y `Grilla01` que las rutas actuales aceptan. Decisión del usuario: **las rutas existentes conservan las entradas que hoy aceptan**. En la primera entrega la definición efectiva se traducía a un `ViewSpec`. `ViewSpec` no puede expresar web controls ni varios estados con contenido, y ampliarlo habría cambiado el contrato del chat. Desde la segunda entrega **las dos entradas producen el mismo plan físico (`ViewCreationPlan`)** y comparten todo lo que viene después: generador, `ViewCreationService` (habilitación, usuario, sesión, ejecución y decisión) y verificador. El plan, el generador y el verificador se ampliaron de forma aditiva: para un plan de un estado sin controles producen exactamente el mismo script y las mismas comprobaciones que antes, y las pruebas heredadas lo fijan sin haber sido modificadas.

- Las reglas del documento (forma, convenciones, soporte) viven sólo en la entrada por documento.
- Las reglas de creación (catálogo, plan, SQL, ejecución, verificación) viven sólo en el camino compartido.
- Los nombres derivados `<estado>F0` y `<área>_Obj` los pone `JsonMetadataPlanFactory`; las rutas por `ViewSpec` escriben el nombre que reciban.
- El catálogo se consulta con el mismo servicio (`ViewCreationPreflightService`): nombre de la vista una vez y alias, SP y firma una vez por grilla.
- Ningún campo se descarta: las rutas por `ViewSpec` no cambian, y el documento rechaza lo que no conoce.

### Capacidades por entrada

| Aspecto | Entrada nueva por archivo (`api/view-json`) | Rutas existentes con `ViewSpec` (`api/view-script`, chat) | Justificación |
|---|---|---|---|
| Formas creables | Vista vacía `site`; `go` con uno o varios estados, una grilla por estado y web controls de navegación | Vista vacía `site`; `go` con un estado y una grilla | `ViewSpec` no expresa controles; no se amplía un contrato que se va a retirar |
| Nombres de fila y de objeto | Derivados como Designer | Los explícitos que traiga el `ViewSpec`; fila `<estado>F0` si no trae | Compatibilidad pedida; el chat no los emite |
| Estado, área y descripción omitidos | `Pantalla`, `Area01`, id de la vista | Sin cambio: estado `Pantalla` sólo para la vista vacía; área obligatoria | No se toca el comportamiento heredado |
| Propiedades desconocidas | Error | Se siguen ignorando al deserializar | Comportamiento actual del chat; endurecerlo es parte de T2 |
| Diagnósticos | Ruta en el documento, severidad, causa y dato necesario | `Gaps` con tema y descripción, como hoy | Contrato del chat y de la pantalla actual |
| Reconocimiento en Designer y Motor | Pendiente. `vDemoGrilla01` creada y verificada en la metadata local; varios estados y controles, sin ejecutar contra una base | `go` con `Fila01` y `Grilla01`: visto en Motor (evidencia manual). Vista vacía: pendiente | — |

### Pruebas que protegen la transición

| Pruebas | Papel | Resultado |
|---|---|---|
| Regresión del chat y del RAG, `ViewSpecPreparationFormatTests`, `ViewScriptGenerationTests`, `ViewCreationTests` (857) | Comportamiento heredado | **Pasan sin ninguna modificación**, también tras ampliar plan, generador y verificador |
| `JsonMetadataTests` (46) | Expansión exacta e idempotente; grilla incompleta; rechazos con ubicación; ausente, `null`, `""` y `[]`; mismo script que la vista vacía heredada y que un `ViewSpec` equivalente; nombres derivados; nombres explícitos heredados; ubicación de rechazos y datos del catálogo | Pasan. Adaptadas en la segunda entrega al plan compartido; se retiraron dos casos que fijaban como «sin soporte» los controles que ahora se materializan |
| `JsonMetadataStatesTests` (49, nuevas) | Varios estados con el inicial por marca; controles definidos una vez y activados por estado; script y verificación de cada estado y de los controles; 32 rechazos con su ubicación; límite de la definición efectiva | Pasan |

No hubo cambios deliberados de nombres en las rutas existentes.

### Pasos

| Paso | Qué cambia | Qué no cambia |
|---|---|---|
| **T0** — hecho | Limpieza del formulario, la plantilla y la vista previa | Todo lo demás |
| **T1** — hecho | Documento, lectura estricta, expansión, validación con ruta, borrador efectivo, mapeo al camino existente, `api/view-json`, diálogo «Crear vista» | `ViewSpec*`, `ChatResult`, parser, normalizador y validador de ViewSpec, router, instrucciones de Copilot, `ViewCreationService`, generador, verificador y todas sus pruebas |
| **T1b** — hecho | Varios estados, una grilla por estado, web controls `buttonList` con `cambiarEstado`; el documento produce el plan directamente; plan, generador, verificador y `ViewCreationService` ampliados de forma aditiva; límite de la definición efectiva | `ViewSpec*`, `ChatResult`, parser, normalizador y validador de ViewSpec, router, instrucciones de Copilot, rutas `api/view-script`, y todas las pruebas heredadas |
| **T2** — lenguaje natural | El chat propone el documento; `ChatResult` transporta el borrador efectivo | RAG, fuentes, conocimiento curado |
| **T3** — retiro | Se retiran `ViewSpec*`, `ViewCreationPlanFactory` y las rutas por ViewSpec; el documento queda como única entrada del plan | — |

En T1, `ViewCreationService` no cambió. En T1b **sí cambió, de forma aditiva**: conserva `CreateAsync(ViewSpec, usuario)`, que ahora delega en una sobrecarga que recibe la preparación del plan; la entrada por documento usa esa sobrecarga. La habilitación, el usuario autorizado, la sesión dedicada, la ejecución y la decisión final son las mismas para las dos entradas.

### Consumidores actuales de `ViewSpec`

Tras la limpieza. Todos siguen activos.

| Consumidor | Qué usa | Naturaleza |
|---|---|---|
| `Contracts/ViewSpec.cs` | `ViewSpec`, `ViewSpecState`, `ViewSpecRow`, `ViewSpecArea`, `ViewSpecParameter`, `ViewSpecHandler` | Definición |
| `Contracts/ViewSpecResult.cs`, `ViewSpecValidationResult.cs`, `ViewSpecKnowledgeGap.cs` | Borrador, validación y huecos de conocimiento | Compartido con RAG |
| `Contracts/ChatResult.cs` | `ViewSpecResult?` | **Contrato del chat** |
| `CopilotChatService` | `ViewSpecInstructions`, `BuildViewSpecChatResult`, intención `ViewCreation` | Chat |
| `ViewSpecResponseParser` | JSON de Copilot → `ViewSpec` y huecos | Chat |
| `ViewSpecNormalizer` | Convenciones de la vista mínima | Chat y generación |
| `ViewSpecValidator` | Errores y pendientes | Chat y generación |
| `ConversationTurnCompletion` | Valida la preparación antes de cerrar el turno | Chat |
| `ConversationCapabilityRouter` | Ejecutor `ViewSpecPreparation` | Chat |
| `DesignerCapabilities` | Texto de capacidades | Chat |
| `ViewScriptController` | Recibe `ViewSpec` en `generate` y `create` | HTTP |
| `ViewCreationService` | `GenerateAsync(ViewSpec, …)` y `CreateAsync(ViewSpec, …)`; llama a normalizador, validador y fábrica de plan | Creación |
| `ViewScriptGenerationValidator` | Formas generables; `ViewSpecParameter` | Creación |
| `ViewCreationPreflightService` | `CheckAsync(ViewSpec)`; `ResolveParameters(IReadOnlyList<ViewSpecParameter>, …)` | Creación |
| `ViewCreationPlanFactory` | `ViewSpec` + preflight → plan | Creación |
| `ViewScriptGenerator` | Entrada `ViewSpec`; resumen | Creación |
| `ViewCreationVerifier`, `ViewCreationPlan` | No reciben `ViewSpec`: trabajan sobre el plan | Creación |
| `Web/Pages/Home.razor` | `_vistaEnProgreso` (`ViewSpecResult`): resumen, validación, «Conocimiento faltante → Enseñar a DesignerIA», y el cuerpo de `generate` y `create` | Web, compartido con RAG |
| Pruebas | `ViewSpecPreparationFormatTests`, `ViewScriptGenerationTests`, `ViewCreationTests`, `ConversationIntegrationTests`, `ConversationCapabilityRouterTests`, `ConversationSubjectResolverTests` | Regresión |

### Ampliar o sustituir

| Campo de ViewSpec | Problema para representar la metadata |
|---|---|
| Todo anulable | Correcto para una propuesta de Copilot; en un documento de autoría obliga a tratar «ausente» y «desconocido» como lo mismo |
| `InitialState` por nombre | La metadata marca el default en cada estado |
| `Row.Name`, `Area.ObjectName` | En Designer son derivados; exponerlos permite nombres que un guardado posterior cambiaría |
| `Area` fusiona área y objeto | Impide título, ayuda, pivot y dimensiones del área |
| `ConfigurationPreset` | Un nombre de preset no describe una configuración |
| `ViewSpecHandler(Type, TargetState)` | No corresponde a `dbo.Handlers` (acción del catálogo, parámetros JSON, orden). La navegación real usa acciones de control o JS |
| Sin controles ni left menu | Son de la vista y se activan por estado; no caben bajo un área |

Ampliar `ViewSpec` obligaría a cambiar el significado de campos que el chat ya produce y que sus pruebas fijan. Por eso se sustituye en la creación y se mantiene en el chat hasta migrarlo.

## 8. Markdown y Copilot: asistencia sobre el mismo contrato

**Tres fuentes, tres papeles.** El esquema define la forma; el código define expansión y validación; los Markdown de `docs/capacidades/` explican reglas y evidencia. Cada ficha cita la regla de código que documenta; si divergen, manda el código y se corrige la ficha. Las fichas están en `docs/`, fuera de `KnowledgeSource`, así que el índice del RAG no las recoge (`KnowledgePaths.GetSourceRoot`). No se añadieron al índice.

**Recorrido posterior con lenguaje natural** (T2, no forma parte del primer paso):

```text
texto → propuesta del mismo contrato → expansión y validación → pendientes concretos
      → corrección del borrador → revisión → creación
```

| Tarea | Quién |
|---|---|
| Proponer el documento a partir del texto; usar `null` para lo mencionado sin precisar | Copilot |
| Consultar evidencia y sugerir cómo resolver un pendiente | Copilot, con las herramientas que ya tiene autorizadas |
| Detectar campos faltantes, referencias rotas y capacidades no soportadas | Código, siempre |
| Comprobar que un alias o un SP existen y cuál es su firma | Catálogo del ambiente. La Wiki no lo sustituye ni prueba que un SP existe |
| Decidir si se puede crear | Backend |

Límites: sin SQL genérico, sin acceso libre a archivos, sin permisos amplios, y sin generar, modificar o ejecutar SP de negocio para resolver un pendiente.

**Salida estructurada con lo instalado**

| Dato | Valor | Fuente |
|---|---|---|
| SDK | `GitHub.Copilot.SDK` 1.0.14 (ensamblado 1.0.14.0) | `DesignerIA.Api.csproj`; `DesignerIA.Api.deps.json` |
| Runtime | GitHub Copilot CLI 1.0.95 | `copilot --version` en este equipo |
| Uso actual | `CreateSessionAsync` + `SendAndWaitAsync` con texto; el JSON se pide por instrucciones y se extrae con `ViewSpecResponseParser`, que tolera una cerca de código | `CopilotChatService` |
| Salida estructurada tipada (`SendAndWaitAsync<T>`, `MessageOptions.ResponseSchema`) | Las notas de la versión `v1.0.15-preview.0` la presentan como función nueva | Notas de versión públicas del SDK |

Conclusión: con la versión instalada **no se puede contar con salida estructurada del SDK**. Aparece en una versión posterior y preliminar, y no se comprobó contra el ensamblado 1.0.14 porque el repositorio no permite inspeccionarlo; tampoco consta si exige una versión mínima del runtime. T2 debe diseñarse con lo que hay —instrucciones, lectura estricta y validación de dominio— y tratar la salida estructurada como una mejora posible que depende de una decisión explícita de actualizar la dependencia. Con o sin ella, toda salida de IA pasa por la validación de dominio: un esquema garantiza forma, no que el alias exista.

## 9. Responsabilidades

Proyectos actuales, sin capas ni interfaces nuevas. Así quedó implementado.

| Regla o tarea | Responsable único |
|---|---|
| Leer el archivo, enviarlo y mostrar borrador, origen de los datos, diagnósticos y confirmación | Web: `Components/JsonMetadataCreateDialog.razor` |
| Abrir el diálogo y mostrar el resultado de la creación | Web: `Pages/Home.razor` |
| Forma del documento, del borrador efectivo y de los diagnósticos | Contracts: `JsonMetadataDocument.cs`, `JsonMetadataDraft.cs` |
| Lectura estricta: JSON, propiedades conocidas, tipos, `null` | API: `JsonMetadataReader` |
| Convenciones de la versión `1.0` | API: `JsonMetadataExpander`, puro |
| Estructura, referencias y soporte | API: `JsonMetadataValidator`, puro |
| Nombres físicos derivados, defaults físicos de los controles y construcción del plan desde el documento | API: `JsonMetadataPlanFactory` |
| Orquestar preparar y crear; ubicar en el documento lo que resuelve o rechaza el catálogo | API: `JsonMetadataService` |
| HTTP | API: `ViewDocumentController` (`api/view-json/prepare`, `create`) |
| Alias, SP, firma y valores, por grilla | API: `ViewCreationPreflightService`, `StringConnectionCatalogService` |
| Configuración de referencia, plan y texto SQL | API: `GridConfigurationPresets`, `ViewCreationPlanFactory`, `ViewScriptGenerator` con `SqlLiteralEscaper` |
| Habilitación, usuario, ejecución, estado final y URL | API: `ViewCreationService` |
| Comprobar lo escrito | API: `ViewCreationVerifier` |
| Borrador conversacional y evidencia | Chat y RAG, fuera del camino de carga |

La constante `Pantalla` tiene un solo dueño: `ViewSpecNormalizer.DefaultStateName`, que la expansión referencia.

## 10. Entregas implementadas: alcance y comprobaciones

### Segunda entrega (T1b)

**Implementada** (ver `DesignerIA-JSONMetadata-Entrega2.md`).

**Incluye:** varios estados, con el inicial identificado por `isDefault`; una fila, un área y una grilla por estado; web controls `buttonList` definidos en la vista, con opciones estáticas y la acción `cambiarEstado`; activación por estado mediante `states[].controls`; comprobación del tipo de control contra el catálogo dentro del script; verificación de cada estado y de cada control; límite de tamaño de la definición efectiva.

**Excluye, con diagnóstico:** segunda fila o área en un estado; estado sin grilla en una vista `go`; los demás tipos de control del contrato (`combo`, `comboExpandable`, `comboCheck`, `calendar`, `calendarMonth`, `buttonClean`, `checkBoxList`, `radioButtonList`, `textBox` y `pivot`); otras acciones (`RefrescarAreas`, `RefrescarWC`, `RefrescarLM`); controles con datos de un SP; título, orden, comodín, visibilidad y modo de un control; pop-up y layout; left menu con contenido; fuentes de parámetro no fijas; gráfico; JSControl; handlers; JS; `settings`.

```json
{
  "schemaVersion": "1.0",
  "view": { "id": "vDemoEstados01", "type": "go" },
  "controls": [
    { "name": "wcIrDetalle", "type": "buttonList",
      "items": [ { "text": "Ir a detalle", "value": "detalle" } ],
      "actions": [ { "name": "cambiarEstado", "parameters": [ "Detalle" ] } ] }
  ],
  "states": [
    { "name": "Principal", "isDefault": true, "controls": [ "wcIrDetalle" ], "rows": [ … ] },
    { "name": "Detalle", "rows": [ … ] }
  ]
}
```

Documento completo: `docs/ejemplos/dos-estados-navegacion.json`.

**Definición, persistencia y visibilidad son tres cosas distintas:**

| Aspecto | Dónde está | Regla |
|---|---|---|
| Definición | `controls[]` de la vista | Una sola vez por control |
| Persistencia | `WebControls.IdEstado` | El estado inicial, como Designer. No aparece en el documento |
| Visibilidad | `states[].controls` → `filters` de `ConfiguracionesEstados` | Por estado; un control puede estar activo en varios estados sin duplicarse |

### Primera entrega (T1)

**Implementada** (ver `DesignerIA-JSONMetadata-Entrega1.md`). No incluye T2 ni T3.

**Incluye:** vista vacía `site`; vista `go` con un estado, una fila, un área y una grilla; parámetros de valor fijo; lectura estricta; expansión; borrador efectivo; carga y revisión en la Web.

**Excluye, con diagnóstico:** segundo estado, fila o área; `settings`; controles; left menu; fuentes de parámetro no fijas; gráfico; JSControl; handlers; JS; pivot; título y ayuda; dimensiones; pop-up; layout; `site` con contenido; `go` sin contenido; seguridad; MyVision.

| Capa | Trabajo | Archivos |
|---|---|---|
| Contracts | Documento, borrador efectivo, anotación y diagnóstico; firma del SP en el resultado de preflight | Nuevos `JsonMetadataDocument.cs`, `JsonMetadataDraft.cs`; `ViewCreationPreflightResult.cs` (un campo opcional) |
| API · documento | Lectura, expansión, validación, mapeo y servicio | Nuevos, en `Services/ViewDocuments/` |
| API · catálogo | Devolver la firma leída del SP | `ViewCreationPreflightService.cs` (tres líneas) |
| API · plan, SQL, ejecución y verificación | Sin cambios | — |
| API · HTTP | `prepare` y `create` por documento | Nuevo `ViewDocumentController.cs`; `ViewScriptController.cs` sin cambios; registro en `Program.cs` |
| Web | Botón «Crear vista», diálogo de carga y revisión, resultado en el panel existente | Nuevo `Components/JsonMetadataCreateDialog.razor`; `Home.razor`, `_Imports.razor`, `app.css` |
| Pruebas | 48 nuevas; ninguna existente modificada | Nuevo `JsonMetadataTests.cs` |

**Comprobaciones en Designer y Motor.** Estado al 11 de octubre de 2026: la número 4 está hecha y la 2 lo está sólo en su parte de metadata. Designer y Motor no están desplegados en este equipo (IIS local sirve únicamente el sitio por defecto y `Motor:BaseUrl` está vacío), de modo que el resto sigue pendiente.

| # | Comprobación | Qué decide |
|---|---|---|
| 1 | Vista vacía (`site`, `Pantalla`, `PantallaF0`, sin `LMFiltros`): ¿Designer la abre sin marcarla incompatible? ¿Motor la carga sin error? | Si la entrada por archivo puede ofrecer la vista vacía, y la decisión D3 |
| 2 | Grilla con `Pantalla`, `PantallaF0` y `Area01_Obj`: ¿Motor muestra los datos? **Hecho en parte:** `vDemoGrilla01` creada; metadata, relaciones y valores verificados por la API y por lecturas independientes; las consultas equivalentes a las de Motor devuelven la estructura. **Falta** verla en Motor | Nombres derivados |
| 3 | Guardar ambas vistas desde Designer y comparar: ¿cambian nombres de estado, fila u objeto? ¿se añade `LMFiltros`? | Estabilidad de las convenciones |
| 4 | Lectura autorizada del catálogo: alias, SP y firma del caso de prueba. **Hecho:** alias `GestionEngine` (`IdStringConnection` 14), SP `[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]`, firma `@QueryString (varchar)`; catálogos `TiposWebControl`, `TipoObjetosDeArea` y `TiposEjecucionesSP` | Datos de catálogo del ejemplo B y de los controles |
| 5 | Creación automática con Motor configurado y apertura por «Abrir vista» | Reserva heredada de V1 |
| 6 | Vista de dos estados con controles: ¿Motor muestra en cada estado sólo los controles de su `filters`? ¿el botón cambia de estado? ¿Designer la abre con los controles asociados a sus estados y la guarda sin cambios? | Segunda entrega completa. No se ejecutó: la escritura autorizada se limitaba a `vDemoGrilla01` |

Deserializar un JSON, pasar pruebas o devolver una URL no acreditan una vista creada. Sin ambiente, la entrega queda con esa reserva escrita.

## 11. Decisiones tomadas para la primera entrega

Las tomó el usuario el 11 de octubre de 2026 y están implementadas.

| # | Decisión | Resuelto |
|---|---|---|
| D1 | Nombre de un área omitido | `Area01`, sólo si la vista tiene una única área |
| D2 | Nombre de un estado declarado sin nombre | `Pantalla`, sólo si es el único estado. El estado inicial se identifica por `isDefault`, nunca por posición |
| D3 | `LMFiltros` en la vista vacía | Se preserva la forma heredada, sin `LMFiltros`; su comprobación en Designer y Motor queda registrada como pendiente |
| D4 | SP con parámetros y documento sin `parameters` | Se lee la firma y se señala cada valor necesario; se conserva la excepción heredada del SP de prueba |
| D5 | Vista vacía por archivo | Se admite como propuesta: sólo identificador y versión. No se declara comprobada |
| — | Rutas existentes | Conservan las entradas que hoy aceptan, incluidos `Fila01` y `Grilla01` |
| — | Controles y left menu | En el contrato son de la vista y los estados los referencian; la asociación física al estado inicial la resuelve el backend. La regla no se generaliza a pop-ups ni a Pivot sin contrastarlos |
| — | Segunda entrega | Se materializan los controles `buttonList` con opciones estáticas y `cambiarEstado`, y varios estados con una grilla cada uno. Pop-up y Pivot siguen como capacidades pendientes, rechazadas con su ubicación. Left menu con contenido, fuentes por selección, varias áreas por fila, gráficos y JS quedan para incrementos posteriores y tampoco se omiten en silencio |
| — | Límite de tamaño | El mismo máximo explícito para el documento recibido y para su definición efectiva |
| 13/10/2026 | JavaScript de vista | Se materializa `view.js` en `Vistas.JsCode`, con la codificación de Designer y verificación posterior. El editor y el script conservan el código; DesignerIA no lo ejecuta. Las demás capacidades antes pendientes siguen rechazadas con su ubicación |
