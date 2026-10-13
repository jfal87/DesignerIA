# JavaScript de vista

`view.js` es texto JavaScript opcional. El JSON contiene el código legible que se edita; el plan físico contiene su representación para `dbo.Vistas.JsCode`.

## Flujo confirmado

- Designer: `KnowledgeSource/DesignerRealEvidence/Scripts/Designer.js`, guardado del editor, sustituye comillas dobles por `\u0022`, comillas simples por `\u0027` y `null` por `\u0000`. Son secuencias de texto, no caracteres nulos.
- Persistencia: `EngineUpdater.GetViewInsertQry` escribe el valor en `Vistas.JsCode`.
- Motor: `sources/Motor.zip`, `FULL/Default.aspx.cs`, `agregarCodigoJavascript`, revierte esas tres secuencias y registra un script en la página. También carga código inline cuando `ArchivoJs` está vacío. `FULL/DefaultV2.aspx` hereda ese code-behind.
- Entrada JSONMetadata: el lector admite el texto; el contrato tipado conserva `Js`; `JsonMetadataPlanFactory` construye `JsCode` con `ViewJavaScriptMetadata.Encode`.
- Generación: `ViewScriptGenerator` usa el mecanismo central de escape SQL para el código codificado. `ArchivoJs` permanece vacío.
- Creación: se vuelve a leer el documento y regenerar el plan. `ViewCreationVerifier` exige el `JsCode` esperado, incluso en la lectura de cada estado. Guardar una vista sin su JS no acredita `Created`.

## Semántica y límites

Omitir `js` conserva el comportamiento anterior: `JsCode` vacío. `"js": ""` también guarda código vacío; `null` es un pendiente y objetos, listas, números y booleanos son errores de tipo.

No se cambian espacios, saltos de línea o nombres de funciones. Se rechazan caracteres nulos reales y cierres literales `</script` porque Motor lo registra dentro de un script HTML. También se rechazan las secuencias literales `\u0022`, `\u0027` y `\u0000` en el código lógico: el decodificador de Motor las sustituiría y podría modificar su significado. El JSON normal puede escapar sus comillas; el lector JSON las convierte en comillas antes de esta comprobación.

DesignerIA no interpreta ni ejecuta el código, ni valida su sintaxis o las APIs de Motor que llama. El código de la demo se comprobó por separado. La ejecución real sucede al abrir la vista en GestionEngine.

La columna heredada es `varchar(max)`; la fidelidad de caracteres fuera del juego de caracteres del ambiente depende de su intercalación, igual que en Designer. El JS del archivo de demo usa ASCII.

## Demo

`docs/ejemplos/dos-estados-con-js.json` describe `vDesignerIA_v2`: los dos estados y controles de navegación existentes, con un indicador visible y un contador actualizado por `clickEnWC`. `docs/ejemplos/vDesignerIA_v2.sql` se obtuvo del endpoint real `api/view-json/prepare`, sin diagnósticos, con catálogo local comprobado. No se ejecutó el SQL ni se creó la vista durante esta entrega.

La prueba en GestionEngine queda a cargo del usuario: abrir Principal, ver “JS activo”, ir a Detalle y volver, y comprobar que el contador sube.
