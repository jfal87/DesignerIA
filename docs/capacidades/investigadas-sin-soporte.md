# Capacidades investigadas y todavía sin soporte

Están analizadas y el contrato reserva su lugar, pero la versión `1.0` **las rechaza con su ubicación**. Las listas de botones con `cambiarEstado` y los varios estados ya se materializan: ver [web-controls.md](web-controls.md) y [estado.md](estado.md). El JS de vista también está soportado: ver [javascript.md](javascript.md). Declararlas no crea nada parcial. El detalle y la evidencia completa están en `../DesignerIA-JSONMetadata-Analisis.md`, secciones B.3 a B.5 y B.9.

| Capacidad | Dónde iría | Qué se sabe | Qué falta para soportarla |
|---|---|---|---|
| Otros controles (combo, calendario, lista, limpiar, casillas, opciones, texto) y otras acciones | `controls[]` de la vista; se activan por estado en `states[].controls` | Designer los inserta en el estado inicial o pop-up; ítems fijos o SP, y si hay ítems Motor no ejecuta el SP; acciones `RefrescarAreas`, `RefrescarWC`, `RefrescarLM`. Catálogo de tipos leído en la base local | Formato del valor en selección múltiple; gramática de `ValoresComodin`; orden de carga; controles con SP |
| Left menu | `leftMenu.tabs[].items[]`; se activa en `states[].leftMenuItems` | Un ítem siempre obtiene su contenido de un SP; su clave es la variable; parámetros bajo el área `leftMenu` | Nombres de los tipos; comportamiento por tipo |
| Fuentes de parámetro | `parameters[].source` con `control`, `leftMenu`, `pivot` o `variable` | La metadata sólo guarda un nombre y un default; el navegador busca ese nombre | Depende de controles y left menu. Una variable libre sólo existe si el JS de la vista la crea |
| Navegación desde una celda | Handler de grilla, o JS | `cambiarEstado` por control ya se materializa | Qué handler habilita el clic en celda |
| Handlers de grilla | `object.handlers[]` por nombre de acción | `Handlers` con acción del catálogo, parámetros JSON y orden. La aplicación ya lee el catálogo de acciones | Plantilla de parámetros por acción; los parámetros nombran columnas del resultado del SP |
| Configuración de grilla | `object.settings` | Claves, tipos y valores en [grilla.md](grilla.md) | Decidir el default y comprobar cada clave en Motor |
| Gráfico | `object.type = "chart"` | Tipo 4; configuración con ejes y series | Render en Motor; forma del resultado del SP |
| JSControl | `object.type = "jsControl"` | Tipo 5; sólo guarda título, ayuda y datos del área | Contrato entre los datos y la función de render |
| Pivot, título y ayuda dinámicos | `areas[].pivot`, `title`, `help` | Controles de tipo 13, 14 y 16 ligados al área | Controles |
| Dimensiones, márgenes, botones de área, panel lateral | Propiedades del área y de la fila | Cómo se guardan | Efecto en Motor |
| Pop-up y layout | `states[].popUp`, `layout` | Cómo se guardan | Efecto en Motor |
| Seguridad, MyVision | `view.usesSecurity`, `parameters[].myVision` | Cómo se guardan | Efecto en Motor |

Fuera del contrato: favoritos, URL cortas, traducciones, comentarios e historial de versiones.

Orden natural de ampliación, según lo que más usan las vistas reales (`vPreciosSemanales_CDA`: 19 controles, 10 ítems de left menu, 98 de 100 parámetros con fuente): hechos, controles con ítems y `cambiarEstado` y varios estados; después, fuentes de parámetro, left menu, varias áreas por fila y handlers. Cada paso necesita su generación, su verificación y su comprobación en Motor.
