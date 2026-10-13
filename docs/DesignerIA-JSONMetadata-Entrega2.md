# DesignerIA — Segunda entrega: prueba real, límite del documento y varios estados con navegación

Fecha: 11 de octubre de 2026. Estado final tras la prueba real de `vDemoGrilla01` y el incremento sobre el mismo contrato. Contrato y reglas: `DesignerIA-JSONMetadata-Contrato.md`. Primera entrega: `DesignerIA-JSONMetadata-Entrega1.md`. Fichas: `capacidades/`. Ejemplos: `ejemplos/`.

## Estado

| Parte | Estado |
|---|---|
| Creación real de `vDemoGrilla01` por el flujo JSON | **Hecha y verificada en la metadata** de `LAPTOP-GM0SHH26` / `GestionEngine` |
| Apertura y guardado en Designer; reconocimiento y datos en Motor; enlace | **No ejecutado:** ninguno de los dos está desplegado en este equipo |
| Límite de la definición efectiva | Corregido y con prueba |
| Varios estados, una grilla por estado y web controls de navegación | Implementado; probado sin escritura en bases. **No ejecutado contra una base:** la escritura autorizada se limitaba a `vDemoGrilla01` |
| Creación temporal | Deshabilitada de nuevo; los archivos de configuración no se tocaron |

Sin escrituras Git, sin dependencias nuevas, sin cambios de autenticación ni publicación. No se modificó ni ejecutó ningún SP de negocio. No se detuvo ningún proceso ajeno.

## 1. Prueba real de `vDemoGrilla01`

Evidencia completa en `artifacts/prueba-real-vDemoGrilla01/` (fuera de Git).

### Qué se ejecutó

| Paso | Resultado |
|---|---|
| Conexión efectiva | Servidor `LAPTOP-GM0SHH26`, base `GestionEngine`, seguridad integrada con la identidad Windows actual, que es además el usuario autorizado para crear. `Motor:BaseUrl` vacío |
| Existencia previa | `vDemoGrilla01` no existía. 20 vistas registradas (`00-antes.txt`) |
| Habilitación | Sólo por variable de entorno (`ViewCreation__Enabled=true`) en una instancia propia de la API en el puerto 5299. Las instancias de Visual Studio (API en 7089/5268 y Web en 7276/5025) siguieron en ejecución sin intervención |
| Preparación | `POST api/view-json/prepare` con `docs/ejemplos/grilla.json` → `canCreate: true`, sin diagnósticos (`02-preparacion-respuesta.json`) |
| Creación | `POST api/view-json/create` con la definición efectiva devuelta → `Created`: «Vista «vDemoGrilla01» creada y verificada en la base de metadata», «Verificados 10 registros, sus relaciones y sus valores», «Este ambiente no tiene una URL base de Motor configurada: la vista se creó sin URL» (`04-creacion-respuesta.json`) |
| Cierre | Instancia propia detenida. En `appsettings.Development.json` la creación sigue en `false` |

La creación se pidió a los puntos de entrada de la API que usa el diálogo, no pulsando en la Web: Visual Studio ocupa los puertos estándar y no se detuvo. El recorrido del diálogo hasta la respuesta de la API se comprobó en vivo en la primera entrega.

### Metadata persistida

Leída después con consultas `SELECT` independientes de la aplicación (`05-metadata-despues-de-crear.txt`).

| Tabla | Registro |
|---|---|
| `Vistas` | `vDemoGrilla01`, tipo `go`. Total de vistas: 21 |
| `Estados` | 2011040 `Pantalla`, inicial |
| `ConfiguracionesEstados` | Configuración sin controles activos |
| `Filas` | 2254792 `PantallaF0`, ancho y alto 0 |
| `Areas` | 2840129 `Area01` |
| `ObjetosDeArea` | 2824684 `Area01_Obj`, tipo 2 (Grilla) |
| `ConfiguracionesObjetoDeArea` | 2819376, configuración de referencia |
| `ExtraccionesDatos` | 25192162 |
| `StoredProcedure` | 14532866 `[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]`, alias `GestionEngine` (`IdStringConnection` 14), tipo de ejecución 1 |
| `LeftMenus` | 6 `LMFiltros` |
| `WebControls`, `ParametrosSP`, `MapeoParametros`, `Handlers`, `VistaVersionado` | 0 filas de esta vista |

Los Ids son los siguientes a los máximos previos. Las consultas equivalentes a las lecturas de Motor (estado inicial, estructura del estado, datos y configuración del objeto), escritas como `SELECT` y no como ejecución de sus procedimientos, devuelven la estructura esperada (`06-lectura-equivalente-motor.txt`).

### Lo que no se pudo ejecutar

| Comprobación pedida | Resultado | Qué falta exactamente |
|---|---|---|
| Apertura en Designer | No ejecutada | Un Designer desplegado que apunte a esta base. IIS local está en ejecución, pero sólo sirve el sitio por defecto: `/Designer/…` responde 404 |
| Guardado desde Designer y comparación antes/después | No ejecutado. Se conserva el «antes»: `05-metadata-despues-de-crear.txt` | Lo mismo. Al disponer de Designer: abrir, guardar sin cambios y volver a ejecutar `05-metadata.sql` para comparar |
| Reconocimiento y datos en Motor | No ejecutado | Un Motor desplegado contra esta base: `/GestionEngine/…` responde 404 |
| Apertura mediante enlace | No ejecutada | `Motor:BaseUrl` configurado; sin él la API no devuelve URL |

Lo comprobado es que la metadata quedó escrita, relacionada y con los valores previstos. **No está comprobado que Designer la abra ni que Motor la muestre.**

## 2. Límite del documento expandido

Un documento compacto podía cumplir el máximo de 256 KB y producir una definición efectiva mayor, que el diálogo enviaba al crear y la API rechazaba por tamaño.

- `JsonMetadataService.ReadDocument` compara ahora el texto de la definición efectiva con el mismo límite explícito (`JsonMetadataReader.MaxContentLength`). Si lo supera añade el error `DocumentoDemasiadoGrande` en la raíz y el borrador no queda habilitado para crear.
- Consecuencia: todo borrador con `canCreate` verdadero puede recibirse otra vez.
- Prueba `EffectiveDocumentOverTheLimitCannotBeCreatedEvenIfTheCompactOneFits`: 6.000 parámetros en una línea caben en el límite; su definición efectiva no.
- No se añadió un segundo límite ni se amplió el existente.

## 3. Incremento: varios estados y web controls de navegación

### Qué se materializa

| Se materializa | Se rechaza con su ubicación |
|---|---|
| Varios estados; el inicial se identifica por `isDefault`, nunca por posición | Estado de una vista `go` sin grilla; segunda fila o área |
| Una fila, un área y una grilla por estado, con el mismo soporte de la primera entrega | `settings`, handlers, gráfico, JSControl, JS |
| Web controls `buttonList` definidos en `controls` de la vista, con opciones estáticas | Los demás tipos del contrato: `combo`, `comboExpandable`, `comboCheck`, `calendar`, `calendarMonth`, `buttonClean`, `checkBoxList`, `radioButtonList`, `textBox` y `pivot` |
| Acción `cambiarEstado` hacia un estado existente | `RefrescarAreas`, `RefrescarWC`, `RefrescarLM` y cualquier otra acción |
| Activación por estado con `states[].controls` | Control con datos de un SP; `title`, `order`, `wildcard`, `visibleIn`, `enabled`, `mode`, `autoHide` |
| Defaults de la versión `1.0` para cada control | Pop-up, layout, left menu con contenido, fuentes de parámetro por selección |

### Reglas, con su evidencia

| Regla | Evidencia |
|---|---|
| Un control se escribe una sola vez, en el estado inicial, con extracción `-1` | `EngineUpdater.CreateWebControls` |
| Cada estado lista sus controles activos en `filters` de `ConfiguracionesEstados` | `EngineUpdater.CreateStateConfig`; exports reales |
| Configuración de la lista de botones: `{"actions":{"cambiarEstado":["<estado>"]}, "SufijoBoton":"false", "visibleEn":"A", "enabled":"true"}` | Texto que escribe Designer |
| La primera opción es la predeterminada cuando no hay comodín | `EngineUpdater.CreateWebControlItems` |
| `LMFiltros` sólo en el estado inicial | `EngineUpdater.CreateLeftMenu` |
| `ButtonList` es el tipo 4 | Catálogo `TiposWebControl` leído en la base local. El script lo comprueba antes de escribir y se detiene si la base tiene otro catálogo |

No se ejecutó ningún SP funcional para descubrir reglas: catálogos por `SELECT` acotado y código de Designer y Motor como evidencia.

### Un solo camino

```text
Archivo JSON → lectura → expansión → validación → catálogo → JsonMetadataPlanFactory ─┐
                                                                                    ├→ ViewCreationPlan → ViewScriptGenerator → ViewCreationService → ViewCreationVerifier
ViewSpec (chat y api/view-script) → validación → catálogo → ViewCreationPlanFactory ┘
```

En la primera entrega el documento se traducía a `ViewSpec`. `ViewSpec` no expresa controles ni varios estados con contenido, y ampliarlo habría cambiado el contrato del chat. Ahora las dos entradas producen el mismo plan y comparten generación, ejecución y verificación. El plan, el generador y el verificador crecieron de forma aditiva: un plan de un estado sin controles produce el mismo script y las mismas comprobaciones que antes.

## Cambios aplicados

Respecto de la recuperación `artifacts/entrega2-estados-20261011/` (líneas añadidas y quitadas):

| Archivo | Cambio |
|---|---|
| `Contracts/JsonMetadataDocument.cs` | +27 −3. `controls` de la vista y del estado; control, opción y acción |
| `Api/Services/ViewScripts/ViewCreationPlan.cs` | +39 −4. Estados adicionales y controles, opcionales |
| `Api/Services/ViewScripts/ViewScriptGenerator.cs` | +130 −42. Un bloque por estado; controles dentro del estado inicial; comprobación del catálogo de tipos |
| `Api/Services/ViewScripts/ViewCreationVerifier.cs` | +189 −40. Conteos, valores por estado y filas de controles y opciones |
| `Api/Services/ViewScripts/ViewCreationService.cs` | +75 −13. Sobrecarga que recibe la preparación del plan; lectura de la verificación por estado |
| `Api/Services/ViewScripts/ViewCreationPreflightService.cs` | +17 −8. Comprobación por grilla |
| `Api/Services/ViewScripts/ViewCreationPlanFactory.cs` | +11 −4. Configuración de estado con controles activos |
| `Api/Services/ViewDocuments/JsonMetadataReader.cs` | +43 −4. Formas de control, opción y acción |
| `Api/Services/ViewDocuments/JsonMetadataExpander.cs` | +6 −2. Orden canónico con `controls` |
| `Api/Services/ViewDocuments/JsonMetadataValidator.cs` | +218 −23. Controles, referencias, estados con grilla |
| `Api/Services/ViewDocuments/JsonMetadataService.cs` | +125 −60. Catálogo por estado, plan directo y límite de la definición efectiva |
| `Api/Services/ViewDocuments/JsonMetadataPlanFactory.cs` | Nuevo, 105 líneas. Sustituye a `JsonMetadataViewSpecMapper.cs` (54 líneas, eliminado) |
| `Api/Controllers/ViewDocumentController.cs` | Ajuste de la llamada a preparar |
| `Api.Tests/JsonMetadataTests.cs` | +39 −41. Ver «Pruebas» |
| `Api.Tests/JsonMetadataStatesTests.cs` | Nuevo, 49 pruebas |
| `docs/ejemplos/dos-estados-navegacion.json` | Nuevo |

Sin cambios: `ViewSpec*`, chat, RAG, conocimiento curado, instrucciones de Copilot, `ViewScriptController`, `ViewScriptGenerationValidator`, Web (el diálogo ya mostraba diagnósticos, orígenes y definición efectiva de forma genérica).

## Ejemplos utilizables

| Archivo | Resultado al preparar en la base local |
|---|---|
| `ejemplos/vista-vacia.json` | Completo |
| `ejemplos/grilla.json` | Error `NombreExistente` en `view.id`: es la vista creada en la prueba real. Para repetirla hay que cambiar el identificador |
| `ejemplos/grilla-incompleta.json` | Seis diagnósticos: 2 por corregir, 3 por completar y 1 sin soporte |
| `ejemplos/dos-estados-navegacion.json` | Completo: estados `Principal` (inicial) y `Detalle`, una grilla cada uno, `wcIrDetalle` activo en `Principal` y `wcVolver` en `Detalle` |

## Comprobaciones

### Compilación y pruebas

| Comprobación | Resultado |
|---|---|
| `dotnet build DesignerIA.slnx` | 0 errores |
| `dotnet test` | **952 superadas, 0 con error** |

| Pruebas | Cantidad | Modificadas |
|---|---|---|
| Heredadas: chat, RAG, `ViewSpec`, generación y creación | 857 | Ninguna |
| `JsonMetadataTests` | 46 | Adaptadas al plan compartido: el documento ya no pasa por `ViewSpec`. Se retiraron dos casos que fijaban `controls` como «sin soporte», porque ahora se materializan; sus rechazos equivalentes están en la clase nueva |
| `JsonMetadataStatesTests` | 49 | Nuevas |

Qué fijan las 49 nuevas:

1. Expansión de varios estados: no se deriva nombre, estado inicial ni nombre de área; idempotente.
2. Plan: estado inicial en segunda posición; filas y objetos derivados; `LMFiltros` sólo en el inicial; `filters` por estado; controles definidos una vez, con su configuración exacta.
3. Un control activo en dos estados no se duplica; `filters` sigue el orden de definición.
4. Script: una inserción por estado en cada tabla; controles entre el alta del estado inicial y su configuración; comprobación del catálogo; avance de cada Id; alias resuelto por grilla; sin construcciones posteriores a SQL Server 2008 R2.
5. El mismo parámetro en dos estados se escribe y se verifica para cada grilla.
6. Verificación: 24 registros para el ejemplo; control fuera del estado inicial, control ausente, configuración de estado distinta y estado sin leer se detectan; un plan sin controles rechaza controles encontrados en la base.
7. 32 rechazos, cada uno con su ubicación, severidad y código: referencias rotas, duplicados, datos que faltan, estado inicial ausente o repetido, capacidades pendientes (otros tipos, pivot, otras acciones, pop-up, left menu, JS) y errores de forma.
8. `site` con varios estados no se admite.
9. Límite de la definición efectiva.

### En vivo, sin escritura

Instancia propia de la API en el puerto 5299 con la creación deshabilitada (`07-validar-sin-escritura.ps1`, `08-preparacion-dos-estados.json`). Detenida al terminar.

| Caso | Resultado |
|---|---|
| `dos-estados-navegacion.json` → preparar | `canCreate: true`. 4 valores completados, 7 de catálogo (identificador disponible; alias, SP y firma de cada grilla) y 8 derivados (fila, objeto y configuración de cada estado; cada control «se escribe una sola vez, en el estado inicial «Principal»») |
| Su definición efectiva → preparar de nuevo | `canCreate: true`, texto idéntico, 1.866 caracteres |
| `vista-vacia.json` | `canCreate: true` |
| `grilla.json` | `NombreExistente` en `view.id` |
| `grilla-incompleta.json` | Los seis diagnósticos de la primera entrega |
| `create` con la creación deshabilitada | 403 «La creación de vistas está deshabilitada en esta instancia» |
| Base después | 21 vistas; `vDemoEstados01` no existe; `vDemoGrilla01` sin web controls |

No se repitió en vivo la regresión del chat y del RAG: su código no cambió en esta entrega y sus pruebas pasan sin modificación.

## Cobertura exacta

| Capacidad | Generada y probada | Escrita en una base | Vista en Designer | Vista en Motor |
|---|---|---|---|---|
| Vista vacía `site` | Sí | No | No | No |
| `go`, un estado, una grilla, nombres derivados | Sí | **Sí** (`vDemoGrilla01`) | No | No |
| Parámetros de valor fijo | Sí | No | No | No |
| Varios estados con una grilla cada uno | Sí | No | No | No |
| Web controls `buttonList` con `cambiarEstado` | Sí | No | No | No |
| `go` con `Fila01` y `Grilla01` (ruta heredada) | Sí | Sí (V1) | No | Sí (evidencia manual de V1) |

## Reservas

1. **Designer y Motor:** nada de esta entrada se abrió en Designer ni se vio en Motor.
2. **Varios estados y controles no se ejecutaron contra una base.** El script se generó y se comprobó por texto y la verificación se probó con lecturas simuladas. Su primera ejecución real necesita autorización para otra vista.
3. **Visibilidad por estado inferida:** que Motor muestre en cada estado sólo los controles de su `filters`, y que el botón cambie de estado, se deduce del código de Designer y Motor; no se observó.
4. **Controles definidos y no activados:** se escriben, como haría Designer, aunque ningún estado los liste. No se advierte al usuario.
5. **Estado sin grilla en una vista `go`:** se rechaza. Un estado vacío junto a otros con contenido no tiene caso comprobado.
6. **SQL Server 2008 R2:** se evitan las construcciones posteriores y hay pruebas por texto. La base local es SQL Server 2022 (16.0, nivel de compatibilidad 160): la ejecución real no acredita la versión antigua.
7. **Heredadas de la primera entrega:** `LMFiltros` en la vista vacía; valor fijo de parámetros no visto en Motor; un alias inexistente produce dos errores en la misma ubicación.

## Siguiente

Sin iniciar, y rechazado hoy con su ubicación: left menu con contenido, parámetros enlazados a selecciones, varias áreas por fila, gráficos y JS; pop-up y Pivot; migración del lenguaje natural al documento (T2). Antes de ampliar conviene la comprobación 6 del contrato: crear la vista de dos estados en una base autorizada y verla en Designer y Motor.
