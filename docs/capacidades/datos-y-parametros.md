# Conexión, Stored Procedure y parámetros

**Significado.** `data` indica de dónde saca sus datos un objeto: un alias de conexión, un SP y los valores de sus parámetros. El SP es responsabilidad del usuario: DesignerIA no lo crea, no lo modifica y no lo ejecuta.

**Ámbito.** `object.data`. En el futuro, también controles e ítems de left menu.

## Propiedades

| Propiedad | Omitirla significa | Default | Quién resuelve |
|---|---|---|---|
| `connection` | No se puede omitir; sin default | — | Usuario el alias; catálogo el `IdStringConnection` |
| `storedProcedure` | No se puede omitir | — | Usuario como `esquema.nombre`; catálogo la forma canónica |
| `parameters` | No declarar parámetros | `[]` | — |
| `parameters[].name` | No se puede omitir | — | Usuario; se contrasta con la firma del catálogo |
| `parameters[].source` | Valor fijo | `{ "kind": "fixed" }` | — |
| `parameters[].default` | No se puede omitir en valor fijo | — | Usuario |
| `parameters[].myVision` | No usa MyVision | Ausencia es el valor | No soportado si se declara |

Una grilla sin SP o sin alias produce un **pendiente** por cada dato. Nunca se convierte en vista vacía ni recibe un valor de ejemplo.

## Tres situaciones que no son lo mismo

| Situación | Documento | En Motor |
|---|---|---|
| No declarar parámetros | `parameters` ausente o `[]` | No se envía ninguno; rige el default del SP |
| Parámetro con valor vacío | `"default": ""` | `''` con comillas, `0` sin ellas. Error en `1.0` |
| NULL de SQL | `"default": null` | No representable con un valor fijo. `null` significa «no lo sé» y es un pendiente |

Si el SP declara parámetros de entrada y el documento no declara ninguno, es un pendiente que enumera la firma leída del catálogo. Se conserva una excepción heredada: `[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]` con su único parámetro `@QueryString`, que es el caso que Motor mostró.

## Lo que resuelve el catálogo

Sólo con lecturas autorizadas: alias único y de SQL Server; conexión configurada para ese alias; SP visible; nombre, tipo, sentido y orden de cada parámetro; al menos dos columnas visibles en el primer resultado. La Wiki no prueba que un SP exista.

## Restricciones de DesignerIA

- Si se declaran parámetros, deben cubrir todos los de entrada.
- Tipos: `varchar`, `nvarchar`, `char`, `nchar`, `int`, `bigint`, `smallint`, `tinyint`, `bit`. Enteros y bit, en decimal estricto.
- Valor: ASCII imprimible, sin `'`, `"`, `\`, `<`, `>`, sin `-mas-` ni `-andpersand-`, y dentro de la longitud del tipo.
- `logon_user` y `gruposvistausuario` no admiten valor: los resuelve Motor.

## Evidencia

- Designer: `EngineUpdater.DataExtraction` (`ExtraccionesDatos`, `StoredProcedure`, `ParametrosSP`), `CreateMappings` (`MapeoParametros`), `validaTipoParametro` (comillas por tipo).
- Motor: `recuperarDatosObjetoDeArea` une el mapeo por estado, área y nombre de parámetro; `Herramientas.recuperarComandoQuery` arma la llamada, quita apóstrofes y aplica `''` o `0` al valor vacío; `PreCoreV2.js` `retornarValorObjetoMotor` devuelve el default cuando no hay fuente.
- Conexión en Motor: `<alias>_<Ambiente>` en su propia configuración; no se puede comprobar desde la base de metadata.

## Cobertura y huecos

- Creable: SP con parámetros de valor fijo. Motor sólo mostró el caso sin parámetros declarados; el valor fijo está generado y probado unitariamente, no visto en Motor.
- No soportado: fuentes `control`, `leftMenu`, `pivot`, `variable`; MyVision; Oracle.
- Pendiente: formato del valor en selección múltiple; uso de `VariableRequest`; SQL Server 2008 R2 para la lectura del primer resultado.
