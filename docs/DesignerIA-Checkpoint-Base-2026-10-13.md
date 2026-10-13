# DesignerIA — Base consolidada y continuidad

**Fecha:** 13 de octubre de 2026. **Repositorio:** `C:\Code\DesignerAI`, GitHub `jfal87/DesignerIA`. **Rama de cierre:** `feature/create-view-executor`; PR solicitado hacia `main`.

Este es el punto de entrada para retomar el trabajo en otro chat. Leer primero `AGENTS.md`, después este documento y, sólo para la capacidad que se vaya a implementar, el contrato y las fuentes pertinentes. Los checkpoints anteriores conservan la historia; este documento aclara el estado más reciente y sustituye las propuestas antiguas de galería, formulario de plantilla y editor permanente dentro de un modal.

## 1. Qué queremos construir

Automatizar la autoría de vistas que hoy se realiza en Designer y que Motor interpreta desde la metadata de GestionEngine.

La dirección acordada es: **la IA ayuda a producir y completar la definición lógica; DesignerIA resuelve referencias, comprueba capacidades y la convierte en metadata mediante reglas conocidas.** La IA no inventa IDs físicos, configuraciones o SQL arbitrario.

JSONMetadata es el documento común de autoría. El usuario debe poder revisarlo, editar sus objetos y corregir el JS antes de generar SQL o crear la vista. Más adelante, texto, Word y conversación producirán ese mismo borrador. Hoy la entrada de creación por documento es determinística; esa integración con IA todavía está pendiente.

Queremos una interfaz amigable que acompañe la automatización. Evitar reconstruir el lienzo y los paneles de Designer como un “Designer V2”. La estructura en acordeones y la edición avanzada progresiva fueron aceptadas por el usuario.

## 2. Recorrido de producto que quedó implementado

```text
Conversaciones: chat y RAG existentes
      │
Preparar vista → modal breve para seleccionar .json
      │
Editor central del borrador
      ├─ Estructura: objetos en acordeones
      ├─ JavaScript: edición, ampliación y salto a funciones
      ├─ Revisión y SQL: pendientes, procedencia y script para copiar
      └─ JSON: edición directa del documento completo
      │
Validar y generar script → revisar → copiar SQL o confirmar Crear vista
```

- La conversación puede abrirse al lado del editor. Es el chat de conocimiento existente; no modifica automáticamente el borrador ni recibe todavía sus cambios como un plan editable.
- El borrador permanece en memoria al cambiar entre Conversaciones y Preparar vista. Recargar la página requiere cargar el JSON guardado; no hay almacenamiento persistente del borrador.
- Guardar JSON descarga el documento actual. Cargar otro archivo lo reemplaza tras su lectura; cancelar conserva el borrador.
- Estructura, JSON y JS comparten el documento. El editor conserva propiedades todavía no soportadas; el backend las rechaza con su ubicación y no crea una vista parcial.
- Toda edición invalida preparación, script y permiso de creación. Una respuesta de preparación anterior a esa edición no habilita Crear.
- JavaScript usa actualmente un textarea ampliable y una lista de funciones declaradas. No es Monaco, no ejecuta código en DesignerIA y no valida su sintaxis.
- La validación y los botones quedan accesibles fuera del contenido que se desplaza. El diseño se comprobó en escritorio y a 735 px, sin desbordamiento horizontal.

## 3. Capacidades de creación actuales

| Capacidad | Estado al cerrar |
|---|---|
| Vista nueva `site` vacía | Soportada |
| Vista `go` con una o varias pantallas | Soportada; cada estado con una fila, un área y una grilla |
| Datos de grilla | SP existente, conexión por alias y comprobaciones del catálogo |
| Parámetros de SP con valor fijo | Soportados dentro de los tipos y límites documentados |
| Controles de navegación | `buttonList` con opciones fijas y acción `cambiarEstado` |
| Controles activos por estado | Referencias por nombre; definición física de controles una sola vez en el estado inicial |
| `view.js` | Soportado en el plan, generación SQL y verificación de creación |
| Copiar SQL y crear desde el botón | Ambos disponibles; creación sujeta a configuración, autorización y confirmación |
| Combo, RefrescarAreas, variables como fuente de parámetros, handlers de grilla | Pendientes; se rechazan, no se omiten |
| Varias áreas por fila, gráficos, left menu con contenido, pop-ups, seguridad | Pendientes según las fichas del contrato |
| Editar o sobrescribir una vista existente | No soportado: se crean vistas nuevas y se rechaza un nombre ocupado |
| Word, TXT, SQL como archivo de entrada, lenguaje natural → JSONMetadata | No implementados |

El JS se conserva legible en el JSON. El plan lo codifica como Designer: comillas dobles → texto `\u0022`, simples → `\u0027`, `null` → `\u0000`; luego el generador aplica el escape SQL central. Motor revierte esas tres secuencias antes de registrar el script.

La verificación de creación compara `Vistas.JsCode` con el plan; una vista guardada sin su JS no acredita `Created`. Se rechazan códigos que el decodificador heredado corrompería o que cerrarían el script inline. La columna histórica es `varchar(max)`: no prometer fidelidad de Unicode ajeno al juego de caracteres del ambiente. La demo usa ASCII.

Los defaults no deben confundirse con conocimiento de negocio. Por ejemplo, nombre de SP y alias no se inventan. Un `null` explícito representa un dato pendiente; no se reemplaza silenciosamente por un default ni representa NULL de SQL.

## 4. Implementación y archivos que conviene conocer

| Responsabilidad | Ubicación |
|---|---|
| Documento y respuesta compartidos | `DesignerIA.Contracts/JsonMetadataDocument.cs`, `JsonMetadataDraft.cs` |
| Lectura estricta, expansión y validación | `DesignerIA.Api/Services/ViewDocuments/JsonMetadataReader.cs`, `JsonMetadataExpander.cs`, `JsonMetadataValidator.cs` |
| Entrada HTTP y preparación | `DesignerIA.Api/Controllers/ViewDocumentController.cs`, `JsonMetadataService.cs`; `api/view-json/prepare` y `api/view-json/create` |
| Traducción al plan físico | `JsonMetadataPlanFactory.cs`, `DesignerIA.Api/Services/ViewScripts/ViewCreationPlan.cs` |
| Catálogo, SQL y creación verificada | `ViewCreationPreflightService.cs`, `ViewScriptGenerator.cs`, `ViewCreationService.cs`, `ViewCreationVerifier.cs` |
| Codificación de JS y literales SQL | `ViewJavaScriptMetadata.cs`, `SqlLiteralEscaper.cs` |
| Navegación y conversación existente | `DesignerIA.Web/Pages/Home.razor` |
| Selector y editor central | `DesignerIA.Web/Components/JsonMetadataCreateDialog.razor`, `JsonMetadataWorkspace.razor` |
| Edición de objetos y estado del borrador | `JsonMetadataNodeEditor.razor`, `DesignerIA.Web/JsonMetadataEditingState.cs` |
| Helpers de foco, descarga y salto a función | `DesignerIA.Web/wwwroot/js/jsonmetadata-editor.js` |
| Diseño y carga del helper | `DesignerIA.Web/wwwroot/css/app.css`, `wwwroot/index.html` |
| Pruebas de documento, estados y JS | `DesignerIA.Api.Tests/JsonMetadataTests.cs`, `JsonMetadataStatesTests.cs`, `JsonMetadataJavaScriptTests.cs` |

Las rutas anteriores con `ViewSpec`, parser y herramientas de Copilot siguen teniendo consumidores en el chat. No eliminarlas hasta terminar su migración explícita al documento común. El formulario de la antigua plantilla, su controller/DTO y la vista previa simulada se retiraron; el detalle está en `DesignerIA-JSONMetadata-Limpieza.md`.

## 5. Qué se comprobó y qué sigue pendiente de prueba

**Confirmado por el usuario en desarrollo:** `vDesignerIA_13102026` se creó, se transportó por SQL a la metadata del ambiente con Motor y se abrió correctamente. El usuario mostró la grilla, los estados Principal/Detalle, los botones Ir a detalle/Volver y la vista en Designer. Esa prueba corresponde al ejemplo de navegación anterior, sin el JS nuevo de la demo.

**Confirmado en esta entrega:**

- API real: preparación de `vDesignerIA_v2`, `CanCreate = true`, cero diagnósticos y SQL generado con JS. Se consultó el catálogo configurado; no se ejecutó el SQL.
- 409 pruebas del área de JSONMetadata, SQL y creación; cero fallos.
- Suite completa al cerrar: **976 pruebas aprobadas, cero fallos** en este entorno local.
- API compiló sin errores, con advertencias anteriores de nulabilidad y paquete redundante. Web compiló sin errores ni advertencias.
- Editor: 20 comprobaciones adicionales de estado/HTTP simulado y pruebas en navegador de carga, JS/JSON, descarga, navegación sin perder borrador, ampliación, funciones y conversación lateral.
- JS de demo: comprobación de sintaxis y simulación de DOM en loading y complete; dos clics actualizan el indicador.

**No confirmado todavía:** ejecución de la demo con JS en GestionEngine. No inferir que ya se probó porque el SQL existe o la API devuelve `CanCreate`.

Las pruebas de RAG leen un corpus local y conocimiento curado ignorados por Git. En una clonación sin ellos no puede repetirse automáticamente la suite completa. Las fixtures calculan la raíz subiendo cuatro niveles desde la salida de compilación; al probar con una salida aislada se usó `artifacts/pr-base-20261013/bin/net10.0/`. No corregir sus datos ni publicar el corpus para satisfacerlas.

Las pruebas de metadata/creación se pueden seleccionar con `FullyQualifiedName~JsonMetadata|FullyQualifiedName~ViewCreation|FullyQualifiedName~ViewScriptGeneration`. El SQL emitido mantiene el objetivo SQL Server 2008 R2; la lectura del primer resultado del SP requiere contraste específico en ambientes antiguos.

## 6. Archivos de demo y prueba manual

- `docs/ejemplos/dos-estados-navegacion.json`: caso anterior que el usuario probó en Motor.
- `docs/ejemplos/dos-estados-con-js.json`: `vDesignerIA_v2`, mismos estados y botones con indicador verde “JS activo” y contador de clics.
- `docs/ejemplos/vDesignerIA_v2.sql`: script preparado por la API real; no ejecutado por el agente. No cambia automáticamente al editar posteriormente el JSON.
- `docs/DesignerIA-JavaScript-Demo.md`: instrucciones y detalle de la entrega de JS.

Para la demo: reiniciar Web y API, cargar el JSON, validar, revisar/copiar el SQL, ejecutarlo manualmente en la metadata de desarrollo y abrir `vDesignerIA_v2`. Comprobar indicador, navegación y contador. Si el nombre está ocupado, cambiar `view.id` y volver a generar. El alias `GestionEngine` y el SP `dbo.PRC_GET_GRID_TEST_SORT_COLUMN` deben existir en el destino, como en la prueba anterior.

Puertos configurados: Web HTTPS 7276 / HTTP 5025; API HTTPS 7089 / HTTP 5268. Las instancias temporales del agente en 5031 y 5033 se cerraron. Inspeccionar el estado real antes de iniciar o detener procesos; no parar Visual Studio del usuario.

## 7. Referencias de inspiración y nuestra adaptación

Estas son referencias de patrones, no dependencias añadidas ni implementaciones copiadas. Para la metadata de GestionEngine, la fuente de verdad sigue siendo Designer/Motor y los exports reales.

| Referencia oficial | Qué tomamos como inspiración |
|---|---|
| [Retool — App builder](https://docs.retool.com/build/apps/concepts/app-builder) | La convivencia de conversación, datos y código en un espacio de trabajo. Nuestra adaptación: borrador central con conversación opcional al lado. |
| [Appsmith — Writing code](https://docs.appsmith.com/write-code/overview) | Lógica JavaScript como sección explícita y editable. Nuestra adaptación actual es una sección JS ampliable, sin adoptar su modelo de JSObjects ni su runtime. |
| [Budibase — Screens](https://docs.budibase.com/docs/screens) y [Components](https://docs.budibase.com/docs/components) | Separación de pantallas y componentes. Nuestra adaptación: jerarquía de estados/filas/áreas/controles, presentada por objetos colapsables. |
| [Fluent 2 — Dialog usage](https://fluent2.microsoft.design/components/web/react/core/dialog/usage) | Diálogos para tareas enfocadas y cautela con formularios largos. Nuestra adaptación: modal de selección breve; edición extensa en la superficie central. |

**Inferencia de producto propia:** la conversación debe completar y corregir un borrador verificable, y el backend debe materializar sólo capacidades conocidas. No afirmar que las plataformas anteriores generan exactamente la metadata de GestionEngine.

Se compararon tres alternativas de interfaz: Espacio central, división permanente y modal amplio. El usuario eligió **Espacio central**. Mantener esta decisión hasta que haya evidencia de uso para revisarla.

## 8. Siguiente entrega recomendada: ejemplo pequeño real

Primero cerrar la prueba de JS de la demo. Después usar `vExampleDesignerIA.sql` como siguiente caso funcional; no empezar con la vista financiera grande.

Ese ejemplo, analizado como evidencia, contiene:

- Tipo `go`, estado `Pantalla8668`, área `area_9728` y grilla.
- SP `PRC_GET_GRID_EXAMPLE_DESIGNERIA` con `ParamUno` ligado a `webControlCombo` y `ParamDos` a `varParamDos`.
- Combo alimentado por `PRC_GET_COMBO_EXAMPLE_DESIGNERIA` y acción `RefrescarAreas`.
- JS que crea/actualiza `varParamDos` mediante `objetoParametro` y `clickEnWC`.
- Handler de grilla y configuraciones que todavía no se representan completamente en el contrato soportado.

**No existe aún un JSONMetadata creable que reproduzca todo ese ejemplo.** El ID físico de conexión de su export no acredita un alias ni es portable entre ambientes. Los cuerpos de sus SP no se obtuvieron de ese export; no inventarlos.

El siguiente encargo debería ser una sola entrega coherente: combo alimentado por SP → valor del control y variable JS → bindings de parámetros → RefrescarAreas → grilla actualizada. Confirmar el alias, firmas, columnas y consumo de Motor, y decidir explícitamente qué hacer con el handler/configuración restante. Cada capacidad declarada debe materializarse o quedar como diagnóstico; nada se descarta para forzar una creación parcial.

Después ampliar según necesidades reales: varias áreas/filas, configuraciones de grilla y handlers, otros controles, left menu/gráficos/pop-ups/seguridad. `sic_budget_costo_variable.sql` es un objetivo posterior de escala: tiene múltiples estados, controles, datos, mappings y un JS extenso; no prometer que hoy se puede reproducir automáticamente.

## 9. Lenguaje natural y documentos, después de esta base

Destino acordado:

```text
Texto / TXT / Word / conversación
             ↓ extracción e interpretación
       Borrador JSONMetadata
             ↓ preguntas y correcciones del usuario
       Validación y resolución de catálogo
             ↓ revisión del SQL
       Creación determinística y verificación
```

La IA sugiere estructura y explica pendientes; C# conserva reglas, autorización, resolución, generación SQL y efectos. El usuario debe ver los cambios antes de crear. No conectar el chat actual al editor suponiendo que su `ViewSpec` ya es el contrato nuevo; esa migración debe implementarse y probarse.

La disciplina de contrastar capacidades contra Designer y Motor seguirá guiando la ingeniería. Un laboratorio automático para aprender capacidades o comparar snapshots es una funcionalidad futura, no parte de este cierre.

## 10. Retomar con poco contexto

Usar un solo chat de implementación con un encargo acotado y este checkpoint. Consultar código y fichas de la capacidad concreta; evitar reenviar ZIPs completos, logs o repetir todo el análisis. Una revisión adicional debe tener un objetivo específico y compartir un resumen, no otro circuito permanente de análisis/ejecución.

Antes de tocar código: leer `AGENTS.md`, inspeccionar rama/status/diff y preservar cambios ajenos. `KnowledgeSource/`, ZIPs aportados y exports son evidencia de lectura, no runtime ni material para publicar. No publicar corpus RAG, configuración local, credenciales o dumps de negocio.

Este cierre autorizó commit, push y PR hacia `main`; no merge, deploy ni SQL de escritura por el agente. Esa autorización no debe asumirse para otro encargo futuro. El MD describe contexto y prioridades, no autoriza automáticamente implementar todas las etapas.

### Texto breve para abrir otro chat

> Trabajamos en DesignerIA, repositorio C:\Code\DesignerAI. Lee AGENTS.md y docs/DesignerIA-Checkpoint-Base-2026-10-13.md y revisa Git antes de cambiar código. La base crea vistas nuevas desde JSONMetadata mediante un editor central con acordeones, JS/JSON sincronizados y revisión SQL; la conversación RAG sigue separada. JS de vista ya se genera en Vistas.JsCode y se verifica, pero debemos confirmar la demo en Motor. El siguiente caso real es vExampleDesignerIA: combo por SP, bindings de control/variable JS y RefrescarAreas. Conserva la interfaz acordada, los cambios preexistentes y los rechazos explícitos de capacidades no soportadas. Usa evidencia y no inventes alias, SP ni metadata. Mi encargo concreto para este chat es: [indicar una entrega y si autorizo análisis o implementación].
