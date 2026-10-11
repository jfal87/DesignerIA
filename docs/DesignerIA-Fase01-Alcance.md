# DesignerIA — Fase 1: Alcance

Fecha: 10 de octubre de 2026.

Referencias acordadas: [Contexto](DesignerIA-Contexto.md) y [Roadmap](DesignerIA-Roadmap.md).

## Necesidad

Automatizar la autoría de vistas y obtener una vista funcionando en Motor.

## Límite de V1

V1 termina en la fase 5: creación automática de una vista nueva, con IdVista y URL.

## Primera plantilla

| Aspecto | Acuerdo |
|---|---|
| Plantilla y referencia | «Consulta con grilla»; tomar sólo la grilla del estado inicial de `vpruebaJFAL.sql`. |
| Estructura | Un estado default, una fila, un área y una grilla. |
| Nombres | `Estado01`, `Fila01`, `Area01` y `Grilla01`. |
| Tipo y versión | TipoVista `go`; versión `1.0`. |
| SP de prueba y alias | `dbo.PRC_GET_GRID_TEST_SORT_COLUMN`; alias `GestionEngine`. |
| Configuración de grilla | Literal de la referencia, preservando sus valores. |
| Configuración de estado | Sin filtros, navegación ni popup; layout `00`. |
| LeftMenu | `LMFiltros` vacío. |

## Exclusiones

Sin WebControls, handlers, gráfico, paginador, JS personalizado ni mapeos explícitos de parámetros.

Si el nombre existe, se rechaza y se solicita otro: sin sobrescritura ni sufijo automático.

## Después de V1

Las fases 6–8 amplían capacidades, entradas y edición conforme al roadmap. Word/lenguaje natural y lectura de vistas para edición convergerán en `ViewSpec`, conforme lleguen sus fases.

## Contrato común

MetaJSON es `ViewSpec` representado en JSON, sin modelo paralelo. Este acuerdo no acredita que el contrato técnico de la primera plantilla esté terminado.

## Checkpoints de comprobación

«Metadata local comprobada» y «Motor comprobado» son checkpoints distintos de la fase 4. El primero no acredita funcionamiento en Motor ni cierra por sí solo esa fase.

## Medición futura

La medición futura compara la autoría con DesignerIA contra clonar, adaptar y comprobar una vista en Designer.

## Criterio de cierre de fase 1

Caso mínimo, exclusiones y destino del roadmap documentados, coherentes entre los tres documentos y revisados.

## Estado

La fase 1 es un acuerdo de alcance. El contrato técnico (fase 2), la generación completa (fase 3), la comprobación (fase 4) y la creación automática (fase 5) no están implementados ni comprobados para esta plantilla. Esta entrega documental no acredita generación ni runtime funcionando y no autoriza implementar las fases 2–8.
