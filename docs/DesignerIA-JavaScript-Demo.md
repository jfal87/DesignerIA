# Demo de vista con JavaScript

Entrega: 13 de octubre de 2026.

## Archivos para la prueba

- JSON: `docs/ejemplos/dos-estados-con-js.json`.
- SQL: `docs/ejemplos/vDesignerIA_v2.sql`.

El JSON usa el identificador `vDesignerIA_v2`, tipo `go`, dos estados con grilla y los botones de navegación `wcIrDetalle` y `wcVolver`. Cada grilla usa el alias `GestionEngine` y el procedimiento existente `dbo.PRC_GET_GRID_TEST_SORT_COLUMN`, como en la prueba anterior.

El JavaScript inicializa un indicador verde con “JS activo” y cuenta los clics recibidos por el hook de Motor `clickEnWC`. La inicialización se registra explícitamente con `DOMContentLoaded` o se ejecuta si el documento ya está listo: no depende de que Motor llame por nombre a `Inicializar`.

## Cómo probar

1. Reiniciar API y Web desde Visual Studio para cargar el soporte nuevo.
2. Abrir Preparar vista y cargar el JSON. El archivo anterior del usuario también conserva su JS y pasa por el soporte nuevo; el JSON de demo añade una señal visible para mostrar su funcionamiento.
3. Validar y generar script. Revisar el SQL y copiarlo. También se puede usar el SQL adjunto, que ya fue preparado con la API real y su catálogo configurado.
4. Ejecutarlo manualmente en la base de metadata del ambiente de desarrollo donde se probó la vista anterior. Requiere el alias y el SP existentes. El script crea una vista nueva y rechaza un nombre ocupado.
5. Abrir `vDesignerIA_v2` en GestionEngine con el estilo V2, comprobar el indicador, ir a Detalle y volver. Los clics deben incrementar el contador.

Si ese identificador ya está ocupado, cambiarlo en el JSON y volver a generar el SQL. El SQL adjunto no cambia al editar el JSON posteriormente.

## Implementación

- Contracts: `JsonMetadataDocument.cs`, miembro opcional `JsonMetadataView.Js`.
- Lectura y expansión: `JsonMetadataReader.cs`, `JsonMetadataExpander.cs`.
- Validación: `JsonMetadataValidator.cs`, comprobaciones de representación compatibles con el decodificador inline de Motor.
- Plan: `JsonMetadataPlanFactory.cs`, `ViewCreationPlan.cs` y el nuevo `ViewJavaScriptMetadata.cs`.
- SQL y comprobación posterior: `ViewScriptGenerator.cs`, `ViewCreationVerifier.cs`.
- Web: `JsonMetadataWorkspace.razor`, mensaje actualizado para el soporte habilitado.
- Pruebas: nuevo `JsonMetadataJavaScriptTests.cs`; se actualizaron los dos casos anteriores que clasificaban JS como no soportado.
- Documentación: contrato JSONMetadata, capacidad Vista y nueva capacidad JavaScript.

## Validaciones y alcance

409 pruebas de JSONMetadata, generación y creación aprobadas. Incluyen tipo de JS, conservación del documento efectivo, codificación y decodificación de Motor, literales SQL, compatibilidad sin JS y detección de JS ausente en la verificación de creación.

API compiló sin errores; conservó advertencias existentes del proyecto. Web compiló sin errores ni advertencias. El código de la demo pasó la comprobación de sintaxis JavaScript y las simulaciones de DOM en estado loading y complete, incluyendo dos clics.

La API real devolvió `CanCreate: true`, cero diagnósticos y un script de 13.239 caracteres. Se llamó únicamente a preparación: lectura del catálogo y generación de texto. No se ejecutó SQL de escritura, no se llamó a Copilot y no se ejecutó la demo en GestionEngine. Los cambios previos del repositorio se conservaron.

Esta entrega habilita JS de vista y mantiene los controles de navegación ya soportados. No incorpora combo, RefrescarAreas, parámetros de variables o handlers adicionales de `vExampleDesignerIA`.
