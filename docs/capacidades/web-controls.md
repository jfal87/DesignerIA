# Web controls de navegación

**Significado.** Un web control es un control de la barra superior de la vista. Esta versión crea listas de botones que cambian de estado.

**Ámbito.** Se **definen** en `controls[]` de la vista y se **activan** por estado en `states[].controls`, con su nombre. Un control no se repite por estado.

## Propiedades

| Propiedad | Omitirla significa | Default | Clase |
|---|---|---|---|
| `controls` | La vista no tiene controles | Ausencia es el valor | — |
| `name` | No se puede omitir | — | Clave funcional: `filters` y el navegador lo usan |
| `type` | No se puede omitir | — | Sólo `buttonList` |
| `items[]` (`text`, `value`) | No se puede omitir | — | Opciones estáticas |
| `actions[]` (`name`, `parameters`) | No se puede omitir | — | Sólo `cambiarEstado` con el nombre de un estado de la vista |
| `states[].controls` | Ningún control activo en ese estado | Ausencia es el valor | Referencias validadas |

Lo escribe el backend por regla, versión `1.0`:

| Dato físico | Valor | Evidencia |
|---|---|---|
| Estado del control (`WebControls.IdEstado`) | El estado inicial | `EngineUpdater.CreateWebControls` |
| Extracción | `-1`: sin SP | Igual; con opciones fijas Motor no ejecuta ningún SP |
| Tipo | 4 `ButtonList` | Catálogo `TiposWebControl` de la base local; el script lo comprueba antes de escribir |
| Orden | Posición en `controls` | Convención de DesignerIA |
| Título y comodín | Vacíos | `Designer.js`, control nuevo |
| Opción predeterminada | La primera | `EngineUpdater.CreateWebControlItems`, sin comodín |
| Configuración | `{"actions":{"cambiarEstado":["<estado>"]}, "SufijoBoton":"false", "visibleEn":"A", "enabled":"true"}` | Texto que escribe Designer |
| Visibilidad por estado | `filters` de `ConfiguracionesEstados`: nombres separados por coma, en el orden de definición | `EngineUpdater.CreateStateConfig` |

## Definición, persistencia y visibilidad

Son tres cosas distintas y sólo dos aparecen en el documento: la definición (vista) y la visibilidad (estado). La persistencia en el estado inicial es un detalle físico que resuelve el backend. Designer hace una excepción para los estados pop-up, que guardan sus propios controles, y para Pivot, ligado a un área: ninguna de las dos se materializa y ambas se rechazan con su ubicación.

## Cobertura y huecos

- Creable: `buttonList` con opciones estáticas y `cambiarEstado`. Ejemplo: `../ejemplos/dos-estados-navegacion.json`.
- No soportado (`type`): `combo`, `comboExpandable`, `comboCheck`, `calendar`, `calendarMonth`, `buttonClean`, `checkBoxList`, `radioButtonList`, `textBox` y `pivot`. Cualquier otro valor es un error: no es un tipo del contrato.
- No soportado: `RefrescarAreas`, `RefrescarWC`, `RefrescarLM`; más de una acción.
- No soportado: `title`, `order`, `wildcard`, `data`, `visibleIn`, `autoHide`, `enabled`, `mode`.
- Un control definido y no activado en ningún estado se escribe igual, sin advertencia.
- **Pendiente:** ninguna vista con controles se escribió en una base. Que Motor muestre en cada estado sólo los controles de su `filters` y que el botón cambie de estado se deduce del código; no se observó.
