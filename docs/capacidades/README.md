# Capacidades de JSONMetadata

Fichas breves, una por capacidad. Formato `1.0`, estado actualizado al 13 de octubre de 2026. El usuario confirmó la navegación en Designer y Motor; la ejecución de la nueva demo con JS queda por comprobar. Ver [base consolidada](../DesignerIA-Checkpoint-Base-2026-10-13.md) y ejemplos en `../ejemplos/`.

| Ficha | Capacidad | Cobertura en `1.0` |
|---|---|---|
| [vista.md](vista.md) | Identificación y propiedades generales | Creable |
| [estado.md](estado.md) | Estados y estado inicial | Varios estados; inicial por su marca |
| [fila-y-area.md](fila-y-area.md) | Filas, áreas y objeto de área | Una fila y un área por estado |
| [grilla.md](grilla.md) | Objeto grilla y su configuración | Configuración de referencia, sin `settings` |
| [datos-y-parametros.md](datos-y-parametros.md) | Conexión, SP y parámetros | Valor fijo |
| [web-controls.md](web-controls.md) | Web controls de la vista y su activación por estado | Lista de botones con `cambiarEstado` |
| [javascript.md](javascript.md) | JS de vista, codificación de Designer y verificación de persistencia | Soportado; demo en Motor pendiente |
| [investigadas-sin-soporte.md](investigadas-sin-soporte.md) | Otros controles, left menu, handlers, gráficoControl, JS y otros | Se rechazan con su ubicación |

## Cómo se mantienen

- **El esquema define la forma, el código define expansión y validación, y estas fichas explican reglas y evidencia.** Si una ficha y el código discrepan, manda el código y se corrige la ficha.
- Cada default lleva su evidencia y su clase: regla comprobada en Designer o Motor, convención de DesignerIA, o pendiente.
- Los informes completos no se repiten aquí: `../DesignerIA-JSONMetadata-Analisis.md` (evidencia) y `../DesignerIA-JSONMetadata-Contrato.md` (contrato, omisiones, recorrido y transición).
- Significado común de ausente, `null`, `""` y `[]`: sección 2 del contrato.

## Uso con el chat

Estas fichas están en `docs/`, fuera de `KnowledgeSource`: **el índice del RAG no las incluye** y no se añadieron a él. Incorporarlas como evidencia del asistente es una decisión aparte. La Wiki y estas fichas explican reglas; no sustituyen al catálogo del ambiente ni prueban que un SP o un alias existen.
