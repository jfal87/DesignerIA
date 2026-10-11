# DesignerIA — Contexto del producto y acuerdos

Fecha del acuerdo: 10 de octubre de 2026.

Este documento explica qué estamos construyendo. El orden de entregas y el encargo del primer análisis están en `DesignerIA-Roadmap.md`. Ambos sirven como contexto inicial para nuevos chats con Codex o Claudio; no hace falta transportar toda la conversación anterior.

## 1. Necesidad del cliente

El cliente quiere: **«No sé cómo, pero quiero que me cree una vista y me dé la URL».**

DesignerIA debe automatizar la autoría de vistas que hoy se construyen manualmente en Designer. El usuario expresa lo que necesita o selecciona una plantilla, completa pocos datos y obtiene una vista que Motor puede abrir por su IdVista.

El destino acordado del roadmap es crear y editar vistas con:

- Grillas.
- Filtros y parámetros de SP.
- Navegación a detalle y varios estados.
- Gráficos.
- JavaScript personalizado asociado a la vista.

DesignerIA será candidato a reemplazar Designer cuando cubra y compruebe esas capacidades. No estamos prometiendo compatibilidad inmediata con todos los componentes históricos ni sustituir Motor.

## 2. Cómo funciona el sistema actual

**Designer** es la aplicación de autoría. Guarda la definición como registros relacionados en GestionEngine y exporta scripts SQL.

**GestionEngine** contiene la metadata que define la vista. La relación principal es:

```text
Vistas
  └─ Estados + ConfiguracionesEstados
       └─ Filas
            └─ Areas
                 └─ ObjetosDeArea + ConfiguracionesObjetoDeArea
                      └─ ExtraccionesDatos
                           └─ StoredProcedure + ParametrosSP

MapeoParametros relaciona estado, nombre de área, SP y fuente del parámetro.
WebControls, LeftMenus y Handlers agregan controles e interacción.
Vistas.JsCode contiene el JavaScript asociado.
```

**Motor** consume esa metadata, recupera la vista por IdVista, carga estados, ejecuta las extracciones configuradas y renderiza los objetos. La base destino debe ser la metadata que consume ese Motor; no se determina por la ubicación de la API.

El exportador de Designer elimina y reinserta la vista, reasignando IDs internos. Los nombres pueden repetirse entre vistas. Sin embargo, los nombres también se utilizan en mapeos, DOM y JS: cambiar uno puede romper referencias aunque las claves numéricas estén bien relacionadas.

## 3. Definición común: ViewSpec representado en JSON

La idea llamada **MetaJSON** será la representación JSON del contrato `ViewSpec`, ampliado por capacidades. No construiremos un segundo modelo paralelo ni un motor genérico que intente soportar todo desde el inicio.

```text
Plantilla y formulario ─┐
Lenguaje natural / Word ├─→ ViewSpec → validación → generación / creación
JSON importado ─────────┤                 └─→ boceto visual
Vista existente ────────┘  (lectura para edición, en una fase posterior)
```

El soporte de importación JSON tampoco está implementado: el esquema anterior muestra cómo pueden converger las entradas futuras.

La definición lógica describe estructura, configuraciones y relaciones. Los IDs físicos de SQL, servidores y cadenas de conexión se resuelven en el backend y pertenecen al ambiente. El modelo usa alias de conexión, sin secretos.

No es requisito almacenar otra copia del JSON en una tabla o archivo. Primero se define un contrato suficiente para la plantilla sencilla; después se amplía con cada capacidad real.

## 4. El comportamiento también forma parte de la vista

En `vPreciosSemanales_CDA.sql` hay ejemplos concretos de:

- `MESDESDE` mapeado a `wcMesDesde`.
- `COD_CALIDAD` mapeado a `lmCalidad`.
- `COD_FAM` mapeado a `varFamilia`.
- `clickEnWC` y otras funciones que actualizan fuentes de parámetros mediante `objetoParametro`.

En el ZIP de Motor se comprobó por lectura de código que:

- `FULL/Default.aspx.cs` incorpora `vista.jsCode` en un bloque de script y restaura `\u0022` y `\u0027` a comillas.
- `FULL/Scripts/PreCoreV2.js` llama a `clickEnWC` cuando está disponible.
- `objetoParametro` crea o actualiza un input oculto.
- El render recoge los valores actuales de los objetos mapeados y los envía por AJAX.
- `SMetadata.cs` recupera los mapeos con parámetro, objeto, área, default y referencia de request.

Es evidencia de implementación, no una prueba ejecutada en Motor. Existen variantes de PreCore; la versión efectiva debe comprobarse en el ambiente de prueba.

Por ello, la definición irá incorporando parámetros, fuentes, defaults y contexto de estado/área/SP. El JS personalizado se podrá incorporar, editar y conservar. Guardarlo no equivale a interpretar o simular cualquier programa: Motor sigue siendo quien lo ejecuta.

JSON, literal SQL y codificación del JS son pasos distintos. Cuando soportemos JS, se verificará que el contenido sobreviva al guardado y recuperación sin alterar comillas, barras, saltos de línea o caracteres. No aplicar reemplazos indiscriminados.

## 5. Experiencia de usuario acordada

```text
Nueva vista → galería de plantillas → popup corto → resumen visual
            → validación → creación → IdVista y URL → abrir Motor
```

La galería comienza con una plantilla: **Consulta con grilla**. El formulario pide nombre/IdVista, descripción, SP y alias de conexión. Los componentes internos y sus configuraciones se generan automáticamente.

Nombres nuevos acordados: `Estado01`, `Fila01`, `Area01` y `Grilla01`, con consecutivos cuando corresponda. No llevan el IdVista como prefijo. Longitudes y usos por nombre se comprueban al concretar el contrato. En edición se conservan las referencias existentes.

Si el nombre/IdVista existe, se avisa y se pide otro. No se sobrescribe ni se agrega automáticamente un sufijo.

El panel derecho usa la misma definición para mostrar un resumen de estructura. Más adelante se amplía a boceto dinámico con datos de ejemplo y navegación soportada. No duplicaremos el runtime de Motor ni ejecutaremos JS personalizado en el boceto.

Para la primera comprobación real se abre la URL de Motor. Incrustarlo en iframe queda fuera de V1 y requeriría verificar acceso y configuración del ambiente.

## 6. Primera plantilla y límite de V1

La referencia es únicamente la grilla del estado inicial de `vpruebaJFAL.sql`, no la vista completa. El export incluye otro estado, gráfico, filtros, handler y JS que no pertenecen a esta plantilla.

Acuerdos para esa plantilla:

- TipoVista `go`; versión `1.0`.
- Un estado default, una fila, un área y una grilla de tipo `2`.
- SP `dbo.PRC_GET_GRID_TEST_SORT_COLUMN`, ejecución tipo `1`.
- Alias de prueba `GestionEngine`; el ID local observado es `14` y se resuelve desde el alias.
- Configuración de grilla tomada literalmente del objeto `area_9728_Obj` de la referencia, preservando sus valores.
- Estado con configuración sin filtros, navegación ni popup; un `LMFiltros` vacío.
- Sin WebControls, handlers, gráfico, paginador ni JS personalizado.
- Sin mapeos explícitos de parámetros. El SP declara `@QueryString varchar(500) = ''`; el usuario indicó que Motor resuelve ese caso. No inventar un mapeo ni fijarle un valor.

El `817` de la configuración procede del default de altura de fila en Designer.js; su efecto visual todavía debe comprobarse en Motor. No hay una lista de columnas configurables confirmada para esta grilla. El SP usa datos de prueba, no datos de negocio en vivo.

**V1 termina al crear automáticamente esta vista nueva y devolver su URL.** Word, lenguaje natural como entrada de creación, edición, iframe, arrastre y capacidades avanzadas se entregan después.

## 7. Ambiente, fuentes y estado actual

Repositorio de trabajo: `C:\Code\DesignerAI`. Leer su `AGENTS.md` antes de analizar o modificar.

Destino local confirmado:

| Dato | Valor |
|---|---|
| Servidor | `LAPTOP-GM0SHH26` |
| Base | `GestionEngine` |
| Autenticación | Windows |
| Configuración API | `GestionEngineLocal` |

La dirección publicada de Motor aún no está definida. Su código admite el patrón `<baseMotor>/DefaultV2.aspx?IdVista=<id>`, pero falta validar la ruta del despliegue real. El acceso al ambiente del cliente está temporalmente indisponible.

Fuentes de dominio principales en `C:\Code\DesignerAI\KnowledgeSource`:

- `vpruebaJFAL.sql` y `vPreciosSemanales_CDA.sql`.
- `DesignerRealEvidence/Clases/EngineUpdater.cs`: reglas de creación.
- `DesignerRealEvidence/Clases/ScriptBuilder.cs`: exportación de registros.
- `DesignerRealEvidence/Scripts/Designer.js`: defaults e interacción de autoría.
- `Imports/ScriptCreateGestionEngine.sql`: esquema SQL, ya incorporado como referencia.

El proyecto ChatGPT también proporciona `Designer.zip`, `Motor.zip` y los SQL bajo `sources/`. Esa carpeta es de sólo lectura. Si un nuevo chat no tiene esos archivos, debe señalar la falta; no asumir que hereda adjuntos de otro chat.

Se realizaron lecturas de código y consultas locales de metadata. No se ha implementado este flujo, aplicado scripts ni probado el render de Motor. La incorporación del esquema a KnowledgeSource fue una copia de referencia, no un cambio de producto. No se reconstruyó el índice después de esa copia.

## 8. Filosofía de implementación

- Código pequeño, explícito y legible; cada responsabilidad en su lugar.
- Controllers delgados; reglas, SQL y efectos en Services; contratos compartidos en Contracts.
- UI en Blazor/Radzen; sin SQL ni lógica de negocio dentro de Razor.
- IA para proponer datos; C# para validar y generar SQL determinístico.
- SQL generado compatible con SQL Server 2008 R2.
- Reutilizar lo existente y evitar capas, patrones o motores genéricos anticipados.
- Consultar evidencia puntual; no cargar ZIPs o reconstruir el RAG para cada creación.
- Una capacidad por entrega. Edición sólo cuando se puedan conservar todos los componentes de la vista.

Los permisos de implementación, aplicación SQL, Git y despliegue se rigen por la instrucción del usuario y el AGENTS.md vigente. Los documentos de contexto no sustituyen esas instrucciones.
