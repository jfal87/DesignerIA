# Grilla

**Significado.** Objeto de área que muestra el resultado de un SP como tabla. Las columnas vienen del SP; la metadata no guarda su lista.

**Ámbito.** `areas[].object` con `"type": "grid"`.

## Propiedades

| Propiedad | Omitirla significa | Default | Clase |
|---|---|---|---|
| `type` | No se puede omitir | — | — |
| `data` | No se puede omitir | — | Pendiente: SP y alias. Ver [datos-y-parametros.md](datos-y-parametros.md) |
| `settings` | Configuración de referencia | La de la versión `1.0` | No se admite declararla todavía |
| `handlers` | Sin handlers | — | No soportado si se declara |

Lo escribe el backend: `ObjetosDeArea` con tipo 2, nombre `<área>_Obj` y sin paginador; una fila en `ConfiguracionesObjetoDeArea` con la configuración de referencia.

## Ejemplo

```json
{
  "object": {
    "type": "grid",
    "data": { "connection": "GestionEngine", "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN" }
  }
}
```

## Configuración de referencia y defaults de Designer

`settings` **no es una bolsa de configuración**: cuando se abra, sólo admitirá las claves de la tabla, con su tipo; una clave desconocida, un tipo incorrecto o un valor fuera del conjunto será un error con su ubicación.

En `1.0` no se admite. La grilla se crea con la configuración de referencia: la del objeto `area_9728_Obj` de `vpruebaJFAL.sql`, que es la única que Motor mostró en una vista creada por DesignerIA. Difiere de la que Designer da a una grilla nueva en las cinco claves marcadas en negrita; cuál debe ser el default general se decide cuando se abra `settings`.

Clave física en `ConfiguracionesObjetoDeArea.Configuracion`. Designer guarda todos los valores como texto; los booleanos, en minúsculas. Fuentes: `Designer.js` `getStructure("grid")`, `EngineUpdater.GetGridConfig`, objeto `area_9728_Obj` de `vpruebaJFAL.sql`.

| Clave física | Tipo | Valores | Designer, grilla nueva | Referencia verificada |
|---|---|---|---|---|
| `TituloArea` | texto | Vacío se guarda como `[]` | `[]` | `[]` |
| `BotonAyuda` | texto | Vacío se guarda como `[]` | `[]` | `[]` |
| `Render` | enumerado | `1` HTML5, `0` HTML, `2` transversal | `1` | `1` |
| `RelacionLM` | texto | — | vacío | vacío |
| `CantRegistrosLoopExcel` | entero | — | `20` | `20` |
| `MaximoTextoAncho` | entero | — | `50` | `50` |
| `ColumnTotalsRenderMode` | enumerado | `RENDER_BK_TIT`, `RENDER_TIT`, `RENDER_BK_TOT`, `RENDER_TOT` | **`RENDER_TIT`** | **`RENDER_BK_TIT`** |
| `ExportarExcel` | enumerado | `0` ninguno, `1` suma, `2` promedio | `1` | `1` |
| `Renglones` | entero | — | `50` | `50` |
| `columnasExcelDescripcion` | booleano | — | `true` | `true` |
| `IdColumnaCodigo` | entero | Índice de columna | `0` | `0` |
| `IdColumnaDescripcion` | entero | Índice de columna | `1` | `1` |
| `Decimales` | entero | — | `0` | `0` |
| `ExpandedRowLevels` | entero | — | `3` | `3` |
| `SeparadorDecimal` | texto | — | `,` | `,` |
| `SeparadorMiles` | texto | — | `.` | `.` |
| `TitulosExcel` | texto | — | vacío | vacío |
| `AnchoColumnas` | entero | — | **`0`** | **`150`** |
| `FixedColumnsMobile` | texto | — | vacío | vacío |
| `RowGroupSeparator` | booleano | — | `false` | `false` |
| `ColumnGroupSeparator` | booleano | — | `false` | `false` |
| `EnableHeader` | booleano | — | `true` | `true` |
| `EnableReference` | booleano | — | `true` | `true` |
| `EnableBody` | booleano | — | `true` | `true` |
| `TitulosFijos` | booleano | — | **`false`** | **`true`** |
| `AggregationTypes` | enumerado | `0`, `1`, `2` | `1` | `1` |
| `ColumnAggregationType` | enumerado | `0`, `1`, `2` | `1` | `1` |
| `MostrarCeros` | booleano | — | **`false`** | **`true`** |
| `IndizarPorColumna` | booleano | — | **`false`** | **`true`** |
| `VisualizarCampos` | booleano | — | `false` | `false` |
| `ResaltarRegistros` | booleano | — | `false` | `false` |

Además, del área y dentro del mismo JSON: `BotonesArea` (`Expandir`, `Excel Sin Formato`), `Hashtag` vacío y `UnidadAltoFila` `817`. Los botones propios de la grilla se guardan como `BotonN:{Clase,ToolTip}`.

## Evidencia

- Designer: `Designer.js` `getStructure("grid")` (propiedades y valores iniciales) y `constants.GridConfig`; `EngineUpdater.GetGridConfig` y `GridProperties` (clave física y forma de guardar).
- Motor: `SMetadata.agregarObjetoDeArea` lee la configuración y sustituye `[]` por el título dinámico o por `<br>`; el tipo llega como `grilla`.
- Referencia: `GridConfigurationPresets.GridQueryConfiguration`.

## Cobertura y huecos

- Creable: grilla con SP, alias y parámetros de valor fijo, con la configuración de referencia.
- No soportado: `settings`, `handlers`, botones propios, título y ayuda dinámicos, relación con left menu.
- Pendiente: forma del resultado que exige cada renderer (`Render` 0, 1 o 2); efecto de `UnidadAltoFila` `817`; comprobar en Motor cada clave antes de abrirla.
