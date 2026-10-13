# Fila, área y objeto de área

**Significado.** Una fila agrupa áreas; un área es un recuadro con nombre que contiene un objeto (grilla, gráfico o control JS). El nombre del área es la clave con la que Motor busca su objeto, su configuración, sus handlers y sus parámetros.

**Ámbito.** `states[].rows[].areas[]`.

## Propiedades

| Propiedad | Omitirla significa | Default | Clase |
|---|---|---|---|
| `rows[].areas` | No se puede omitir | — | Una fila sin áreas es un error |
| `areas[].name` | Nombre por convención | `Area01`, sólo si la vista tiene una única área | Convención acordada para V1 |
| `areas[].object` | No se puede omitir | — | — |
| `object.type` | No se puede omitir; sin default | — | `grid`, `chart`, `jsControl` |

No son propiedades; los deriva el backend como Designer:

| Dato físico | Regla | Evidencia |
|---|---|---|
| Nombre de fila | `<estado>F<índice>` | `EngineUpdater.CreateRows`; tres exports |
| Nombre de objeto | `<área>_Obj` | `EngineUpdater.CreateAreaObject`; tres exports |
| Ancho y alto de fila y de área | 0 | Exports; Motor convierte 0 en 100 (`SMetadata.agregarEstado`) |

Restricciones de DesignerIA para el nombre de área: `[A-Za-z0-9_]`, hasta 75 caracteres, único en la vista, distinto de `divWebControls` y `leftMenu`.

## Evidencia

- Designer sanea el nombre con `ParseID` y, si queda vacío, usa un id generado al azar (`area_NNNN`).
- Motor: `recuperarDatosObjetoDeArea`, `recuperarConfiguracionObjetoDeArea` y `recuperarHandlersObjetoDeArea` filtran por `Areas.Nombre` dentro del estado.
- `divWebControls` y `leftMenu` son los nombres de área con que se guardan los parámetros de controles y de left menu.
- Tipos de objeto: grilla 2, gráfico 4, JSControl 5 (`EngineUpdater.GetAreaObjectDBKey`). Para un tipo desconocido el código cae en grilla; DesignerIA no copia ese respaldo.

## Cobertura y huecos

- Creable: una fila con un área y una grilla en cada estado. Con varias áreas en la vista, cada una necesita nombre; no puede coincidir con el de un web control.
- No soportado: segunda fila o área en un mismo estado; `mode`, `height`, `width`, márgenes, `rowHeight`, `buttons`, `hashtag`, `title`, `help`, `pivot`.
- Pendiente: si Motor depende de los nombres de fila y de objeto. La V1 usó `Fila01` y `Grilla01` y Motor la mostró; los exports usan los derivados.
- Pendiente: efecto en Motor de dimensiones, unidades, botones de área y panel lateral (`Filas.Ancho` = 1).
- `position` y `enabled` existen en Designer pero no se guardan.
