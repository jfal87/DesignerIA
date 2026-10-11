# DesignerIA — Roadmap, V1 y primer análisis

Nota de publicacion: los identificadores de infraestructura y cuentas de Windows se sustituyeron por marcadores; el documento original se conserva en el equipo de desarrollo.

Acuerdo del 10 de octubre de 2026. Leer primero `DesignerIA-Contexto.md` para entender el producto y su evidencia. Este documento establece orden, alcance y criterios de cierre.

## 1. A dónde llegan las ocho fases

**Meta final:** crear y editar vistas con grillas, filtros y parámetros, navegación a detalle, gráficos y JS personalizado; materializarlas en GestionEngine y abrirlas mediante una URL de Motor.

**Meta de V1:** una sola plantilla de consulta con grilla, creada automáticamente, con IdVista y URL. Se alcanza en la fase 5. Las fases 6–8 amplían capacidades, formas de entrada y edición.

El plazo inicialmente planteado fue el jueves 15 de octubre de 2026 para demostrar la primera grilla. No es una fecha comprometida para completar las ocho fases. La prueba final en Motor depende de recuperar acceso o disponer de un ambiente accesible.

## 2. Roadmap fijo

| Fase | Qué hacemos | Criterio de cierre |
|---|---|---|
| **1. Alcance** | Elegir la grilla de referencia y fijar las metas | Caso mínimo, exclusiones y destino del roadmap acordados |
| **2. Plantilla y contrato** | Contrastar referencia, esquema y reglas de Designer; concretar ViewSpec | Campos, defaults, nombres, componentes y relaciones de la primera plantilla definidos con evidencia |
| **3. Generación completa** | Galería, popup, resumen estructural y generación determinística | El formulario produce el script completo de una vista nueva; no genera SQL parcial ni ejecuta escritura |
| **4. Comprobación** | Aplicar una prueba autorizada y revisar relaciones; abrir en Motor | Metadata coherente y vista mostrando los datos esperados en Motor |
| **5. Creación automática y URL** | Incorporar creación desde API y resultado para el usuario | Crea una nueva vista sin sobrescribir, informa fallos y devuelve IdVista y URL. **Cierre de V1** |
| **6. Capacidades y configuración visual** | Agregar parámetros, filtros, navegación, gráficos y JS por entregas pequeñas | Cada capacidad tiene contrato, generación, UI necesaria y prueba en Motor; el boceto refleja lo soportado |
| **7. Entrada asistida** | Lenguaje natural y después Word | Preparan el mismo contrato, preguntan datos faltantes y usan la misma validación y creación |
| **8. Edición** | Leer una vista compatible, modificarla y guardar | Se conservan estructura, configuración, relaciones y JS; los componentes no soportados se detectan antes de permitir editar |

Estado al 10 de octubre de 2026: fases 1 y 2 cerradas; fase 3 cerrada funcionalmente en el ambiente local, con generación, pruebas y comprobación HTTP y visual reportadas en `DesignerIA-Fase03-Informe.md`. El script generado no se ejecutó: no hay metadata aplicada ni comprobación en Motor. Fases 4–8 no implementadas; el punto de partida de la fase 4 está en `DesignerIA-Fase04-Checkpoint.md`. La tabla de la sección 4 describe el código anterior a la fase 3. No confundir un script generado con una vista creada.

La fase 4 tiene dos checkpoints: **metadata local comprobada** y **Motor comprobado**. El primero permite avanzar en trabajo local, pero no cierra por sí solo la fase ni acredita funcionamiento del runtime.

## 3. Cómo se construye V1

```text
Galería → plantilla Consulta con grilla → popup
       → ViewSpec completo → validación y comprobaciones del ambiente
       → resumen estructural → script generado                    [fase 3]
       → aplicación de prueba → queries → comprobación en Motor   [fase 4]
       → creación automática → IdVista + URL                     [fase 5]
```

Arquitectura esperada, reutilizando los elementos que ya existan:

```text
Web (formulario y presentación)
  → HTTP / Controller delgado
    → Services (plantilla, validaciones, generación o creación)
      → SQL Server

Contracts / ViewSpec es compartido entre Web y API.
El resumen y el generador consumen la misma definición.
```

No agregar otro proyecto, otra jerarquía de modelos ni un framework genérico de plantillas. El nombre MetaJSON no exige una nueva arquitectura.

### Datos y defaults acordados

| Dato | Responsable |
|---|---|
| Nombre / IdVista | Usuario; se comprueba colisión y se pide otro si existe |
| Descripción | Usuario; editable con propuesta inicial basada en el nombre |
| SP y alias | Usuario; primera prueba con `PRC_GET_GRID_TEST_SORT_COLUMN` y `GestionEngine` |
| TipoVista | Plantilla: `go`, precargado; no es el default general de Designer |
| Estado, fila, área y objeto | Plantilla; nombres `Estado01`, `Fila01`, `Area01`, `Grilla01` |
| Configuración de grilla | JSON de la grilla inicial de `vpruebaJFAL`, sin normalizar sus valores |
| Configuración de estado | Sin filtros, leftMenus activos, popup ni navegación; layout `00` |
| LeftMenus | Una fila `LMFiltros` vacía, según acuerdo de compatibilidad |
| IDs físicos y conexión numérica | Backend y base destino; no los escribe el usuario |
| JS y mapeos de parámetros | Vacíos para esta primera plantilla |

La etiqueta histórica «V1» dentro del validador actual significa generación mínima de estado vacío. No confundirla con la **V1 de producto acordada en estos documentos**, que llega hasta grilla y creación automática.

### Reglas de creación

- Se crea una vista nueva; no se ejecuta el export completo de `vpruebaJFAL` ni se llama a eliminarVista sobre una vista existente.
- La colisión se revisa en la preparación y se vuelve a controlar al crear; el resultado debe reflejar lo ocurrido en la base.
- La creación automática debe ser consistente: tratar fallos, concurrencia en asignación de IDs y registros parciales con una solución proporcional y compatible con SQL 2008 R2.
- No declarar éxito por HTTP 200, por obtener un script o por construir una URL.
- La URL depende de una base de Motor configurada. No inventar un host ni presentar una liga construida como comprobada.

## 4. Estado del código que debe contrastarse

Lectura realizada durante esta definición; comprobar diferencias recientes antes de modificar:

| Elemento | Situación observada |
|---|---|
| `DesignerIA.Contracts/ViewSpec.cs` | Describe vista, estados, filas, áreas y handlers; aún no describe toda la configuración de grilla ni sus parámetros |
| `Services/ViewScripts/ViewScriptGenerationValidator.cs` | Sólo admite un estado inicial/default con cero filas lógicas |
| `ViewCreationPlanFactory.cs` / `ViewCreationPlan.cs` | Plan mínimo existente, con fila física y configuración de estado |
| `ViewScriptGenerator.cs` | Genera Vistas, Estados, Filas y ConfiguracionesEstados; TipoVista fijo `site` |
| `ViewCreationPreflightService.cs` | Comprueba colisión por Nombre/IdVista, no existencia del SP y sus parámetros |
| `StringConnectionCatalogService.cs` | Ya existe resolución de alias a ID; localizar y reutilizar el servicio |
| `Controllers/ViewScriptController.cs` | Endpoint actual `POST api/view-script/generate` |
| `DesignerIA.Web/Pages/Home.razor` | Chat, resumen de ViewSpec y generación; Crear vista está deshabilitado |

Faltan para la grilla: Areas, ExtraccionesDatos, StoredProcedure, ObjetosDeArea, ConfiguracionesObjetoDeArea y el LeftMenu acordado, además de las relaciones y validaciones correspondientes. No introducir ParametrosSP/MapeoParametros por anticipación en este caso sin mapeos.

No se comprobó que Git estuviera limpio. El último checkpoint observado fue `74ca306` en `feature/knowledge-improvements`; es una referencia histórica, no una afirmación sobre el estado del nuevo chat. Inspeccionar estado y diferencias antes de implementar y conservar cambios ajenos.

## 5. Encargo del primer análisis para Claudio y Codex

**Objetivo:** terminar el contraste técnico de fase 2 y producir un plan pequeño y revisable de implementación de fase 3. Este primer encargo es de análisis: no modificar código, ejecutar SQL de escritura, hacer build, cambiar Git ni desplegar.

Repositorio: `C:\Code\DesignerAI`. Primero leer `AGENTS.md` y revisar el estado actual. El ambiente de metadata definido es `SQL_LOCAL_HOST / GestionEngine`, autenticación Windows, configuración `GestionEngineLocal`. No imprimir cadenas de conexión ni credenciales. El análisis puede avanzar con las fuentes sin consultar SQL; cualquier consulta debe respetar el alcance de lectura autorizado.

Lecturas necesarias, sin auditar el resto del RAG:

1. `ViewSpec` y flujo actual de generación/preflight/UI.
2. `vpruebaJFAL.sql`, sólo para extraer y contrastar la grilla inicial y sus dependencias.
3. `EngineUpdater.cs` y `Designer.js`, sólo en los métodos y defaults que expliquen esas filas.
4. `ScriptCreateGestionEngine.sql`, sólo para columnas, longitudes, claves y tipos pertinentes.
5. Motor únicamente si hay una duda concreta del contrato que no resuelvan esas fuentes.

Entregable del análisis:

- Tabla con cada componente y relación requeridos, su origen y lo que falta en el generador.
- Propuesta mínima de ampliación de `ViewSpec`, sin implementarla todavía.
- Validación de los nombres acordados, sus longitudes y referencias por nombre.
- Lista corta de archivos a modificar y responsabilidades por archivo.
- Comprobaciones faltantes de SP/alias y cómo reutilizar lo existente.
- Criterios de prueba de generación, colisión y coherencia de relaciones.
- Dudas realmente bloqueantes, con evidencia concreta; no repetir decisiones ya acordadas.

Resultado esperado: un plan concreto de fase 3, no una propuesta nueva de arquitectura ni una reescritura del roadmap.

### Reparto recomendado

Claudio prepara el contrato y plan a partir del código actual y las reglas de creación de Designer. Codex contrasta las columnas, valores y relaciones contra el export y el esquema. Es una revisión de la misma solución; no dos implementaciones paralelas.

Si se trabaja en chats separados, el usuario comparte los resultados. Un archivo de contexto no autoriza herramientas para enviar mensajes a otros chats ni supone memoria compartida.

## 6. Cómo crecen las capacidades después de V1

La fase 6 se divide en entregas pequeñas; no comienza con un editor completo:

1. Parámetros con valores fijos.
2. Parámetros ligados a WebControls y LeftMenus.
3. Varios estados y navegación a detalle.
4. Gráficos.
5. Variables actualizadas desde JS y JS personalizado asociado.

El orden interno podrá ajustarse según evidencia y uso sin alterar las metas de las ocho fases. Para parámetros desde JS, la prueba debe recorrer clic → fuente actualizada → valor enviado por AJAX → resultado esperado en Motor.

Antes de editar vistas históricas con borrado/reinserción, se necesita lectura completa y comprobación de conservación. No renombrar ni eliminar silenciosamente componentes o referencias que el modelo todavía no soporte.

## 7. Contexto pequeño por fase

Cada chat comienza con estos dos documentos y un checkpoint breve:

```text
Fase activa:
Entrega puntual:
Estado del repositorio:
Archivos involucrados:
Qué ya se comprobó:
Qué queda pendiente:
Acciones autorizadas para este encargo:
```

Al cerrar la fase, informar qué cambió, qué se probó, qué no se pudo probar y qué necesita la siguiente. No transportar logs completos, conversaciones antiguas ni el contenido íntegro de KnowledgeSource.

Las mejoras ajenas a la entrega se registran para después. Los datos operativos que faltan —base publicada de Motor, acceso y autorización de aplicación— se resuelven cuando la fase los necesite; no amplían el alcance funcional acordado.
