# Estado

**Significado.** Un estado es una pantalla de la vista. Motor abre el estado inicial y puede cambiar a otro por su nombre.

**Ámbito.** `states[]`, a nivel de vista.

## Propiedades

| Propiedad | Omitirla significa | Default | Clase |
|---|---|---|---|
| `states` | Vista vacía | Un estado `Pantalla`, inicial, sin filas | Convención de DesignerIA (heredada) |
| `name` | Nombre por convención | `Pantalla`, sólo si es el único estado | Convención de DesignerIA |
| `isDefault` | Estado inicial por regla | `true`, sólo si es el único estado | Regla de Motor: exactamente uno |
| `rows` | Estado sin contenido | `[]` | Designer crea el estado con `Filas: []` |

Con varios estados no hay defaults: cada uno necesita nombre y exactamente uno debe declarar `isDefault: true`. **No se elige el primero.** El estado inicial puede estar en cualquier posición.

`controls` lista por nombre los web controls de la vista activos en ese estado ([web-controls.md](web-controls.md)). Omitirlo significa ninguno.

`states: []` es un error: se omite la propiedad o se declara un estado.

Lo escribe el backend por regla: `Estados.Descripcion` = nombre, `ArchivoJs` vacío, `LeftMenuFijo` 0; una fila en `ConfiguracionesEstados` con `{"filters":"<controles activos>","leftMenus":"","isPopUp":false,"popUp":{},"idLayout":"00"}`; `LMFiltros` vacío sólo en el estado inicial con grilla; y, si el estado no tiene filas, una fila física `<estado>F0`.

## Evidencia

- Designer nombra el primer estado `Pantalla` más cuatro dígitos al azar (`Designer.js` `new`, `addVisualization`, `getRandomID`). `Pantalla` sin sufijo es la convención determinista de DesignerIA (`ViewSpecNormalizer.DefaultStateName`).
- El nombre es clave funcional: `recuperarEstructuraEstado` lo acepta en lugar del id, y la acción `cambiarEstado` lo usa.
- Motor sólo encuentra un estado que tenga al menos una fila: `recuperarEstructuraEstado` une con `Filas` por `INNER JOIN`. De ahí la fila física del estado vacío. Designer no la crearía.
- Designer inserta `LeftMenus` `LMFiltros` en todo estado inicial o pop-up (`EngineUpdater.CreateLeftMenu`). La grilla de DesignerIA la inserta; la vista vacía conserva la forma heredada, sin ella, hasta comprobarla en Designer y Motor.

## Cobertura y huecos

- Creable: uno o varios estados. En una vista `go` cada estado lleva una grilla; la vista vacía `site` tiene un único estado.
- Escrito en una base: un estado con grilla (`vDemoGrilla01`, 11 de octubre de 2026). Varios estados: generado y probado, sin ejecutar.
- No soportado: estado sin grilla en una vista `go`; `leftMenuItems`, `popUp`, `layout`.
- Pendiente: que Designer y Motor reconozcan estas vistas; efecto de `idLayout` y del pop-up.
