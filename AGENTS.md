# DesignerIA — AGENTS.md

## 1. Propósito

Este archivo define **cómo debe trabajar cualquier agente de desarrollo dentro de DesignerIA**.

No describe una tarea puntual, un checkpoint ni una feature específica.

Las tareas concretas, endpoints, pruebas, contratos esperados y alcance funcional deben llegar en el **prompt de trabajo**.

Regla mental:

```text
AGENTS.md = cómo trabajar siempre
Prompt     = qué hacer ahora
```

El agente debe cumplir ambos.

---

## 2. Fuente de verdad y alcance local

La única ruta autorizada para el desarrollo local es:

```text
C:\Code\DesignerAI
```

Fuentes de verdad, en este orden:

1. instrucción actual del usuario;
2. este `AGENTS.md`;
3. código y configuración existentes dentro de `C:\Code\DesignerAI`.

No inspeccionar, copiar, reutilizar ni adaptar código de otros proyectos locales o repositorios externos para ahorrar trabajo.

No utilizar como referencia interna:

- Lumed;
- Portal;
- ViewDesignerSolution;
- Designer;
- Motor;
- otros proyectos bajo `C:\Code`;
- repositorios de GitHub;
- Azure DevOps;
- Azure Repos;
- proyectos de terceros.

Si aparece información incidental de otro proyecto, ignorarla.

Para aclarar una API pública de una dependencia ya autorizada, se puede consultar **documentación pública oficial del proveedor**.

No decompilar DLLs, no usar ILSpy, reflexión exploratoria, herramientas auxiliares de ingeniería inversa ni inspeccionar cachés globales de NuGet.


### 2.1 `KnowledgeSource` como evidencia autorizada

La carpeta:

```text
C:\Code\DesignerAI\KnowledgeSource
```

puede contener documentación, Wiki, scripts SQL exportados, ejemplos reales y copias aisladas de código fuente usadas exclusivamente como **evidencia de dominio**.

Reglas:

- el agente puede leer y analizar estas fuentes porque están dentro de la ruta autorizada de DesignerIA;
- `KnowledgeSource` es evidencia/referencia, no código productivo de DesignerIA;
- no compilar, referenciar como proyecto, enlazar ni incorporar directamente archivos de `KnowledgeSource` al runtime de DesignerIA salvo autorización explícita;
- no copiar código antiguo de forma mecánica: extraer reglas de dominio y reimplementarlas de forma simple, controlada y consistente con DesignerIA;
- para comportamiento técnico, preferir evidencia directa de implementación y artefactos reales producidos por el sistema sobre interpretaciones documentales cuando exista una contradicción;
- conservar y documentar contradicciones entre fuentes; no ocultarlas ni resolverlas inventando una regla;
- una instrucción explícita del usuario sobre el ambiente o alcance actual siempre tiene prioridad.

---

## 3. Stack y arquitectura base

DesignerIA es una solución .NET 10 compuesta por:

```text
DesignerIA.slnx
├── DesignerIA.Web
├── DesignerIA.Api
└── DesignerIA.Contracts
```

Stack base:

- .NET 10;
- C#;
- Visual Studio 2026;
- Blazor WebAssembly;
- ASP.NET Core Web API;
- Radzen.Blazor;
- CSS propio;
- `Microsoft.Data.SqlClient`;
- GitHub Copilot SDK cuando la tarea lo requiera.

Dependencias:

```text
DesignerIA.Web       -> DesignerIA.Contracts
DesignerIA.Api       -> DesignerIA.Contracts
DesignerIA.Web       -X-> DesignerIA.Api como referencia de proyecto
DesignerIA.Web       -> DesignerIA.Api únicamente mediante HTTP
```

`DesignerIA.Web` nunca debe conectarse directamente a SQL ni al GitHub Copilot SDK.

SQL y Copilot deben vivir exclusivamente en `DesignerIA.Api`.

No crear proyectos nuevos ni cambiar esta arquitectura sin autorización explícita.

---

## 4. Principio de diseño

El código debe ser:

- simple;
- explícito;
- legible por humanos;
- consistente;
- fácil de depurar;
- fácil de modificar;
- preparado para crecer sin sobrearquitectura.

Preferir **código aburrido y evidente** sobre código ingenioso.

No usar una solución compleja cuando una solución directa resuelve correctamente el problema.

Evitar salvo necesidad real:

- Clean Architecture ceremonial;
- CQRS;
- MediatR;
- Unit Of Work;
- repositories genéricos;
- factories innecesarias;
- wrappers triviales;
- microservicios;
- herencia innecesaria;
- reflexión;
- metaprogramación;
- service locator;
- helpers genéricos sin una necesidad concreta;
- abstracciones creadas únicamente "por si sirven después".

Escalable no significa agregar capas anticipadamente.

Escalable significa mantener responsabilidades claras para que una pieza pueda evolucionar sin romper las demás.

---

## 5. Consistencia y estándares de programación

La solución debe mantenerse **homogénea**.

Responsabilidades equivalentes deben resolverse con el mismo patrón mientras ese patrón siga siendo válido.

Antes de implementar algo nuevo, revisar cómo DesignerIA resuelve una responsabilidad equivalente y seguir ese estilo.

No hacer esto:

```text
Controller A -> Service -> SQL
Controller B -> 200 líneas de lógica + SQL dentro del Controller
```

La misma responsabilidad debe resolverse de forma consistente.

### 5.1 C#

Usar convenciones estándar de .NET/C# y las configuraciones existentes del proyecto.

Reglas:

- `PascalCase` para tipos, propiedades y métodos públicos;
- `camelCase` para parámetros y variables locales;
- nombres descriptivos, no abreviaturas crípticas;
- métodos async terminan en `Async`;
- usar `async/await` para I/O;
- propagar `CancellationToken` cuando exista una operación I/O relevante y el flujo actual lo soporte;
- evitar métodos excesivamente largos;
- evitar clases gigantes con múltiples responsabilidades;
- no duplicar lógica;
- no ocultar comportamiento importante dentro de extensiones o helpers innecesarios;
- no introducir patrones distintos para el mismo problema sin una razón técnica clara.

No crear una interfaz para cada servicio por costumbre.

Crear interfaces únicamente cuando exista una razón real, por ejemplo:

- varias implementaciones;
- contrato reutilizable real;
- necesidad clara de desacoplamiento;
- estrategia de pruebas que realmente lo justifique.

### 5.2 Controllers

Los Controllers deben ser delgados.

Responsabilidades permitidas:

```text
HTTP request
   -> validación de transporte mínima
   -> llamada a Service
   -> HTTP response
```

No colocar en Controllers:

- acceso SQL;
- lógica del Copilot SDK;
- lógica de negocio importante;
- procesamiento complejo;
- construcción extensa de objetos;
- reglas de dominio.

Si existe lógica real, moverla a un Service específico.

### 5.3 Services

Crear Services únicamente para responsabilidades reales.

Un Service debe:

- tener un nombre específico;
- tener una responsabilidad clara;
- concentrar lógica relacionada;
- ser pequeño y legible;
- usar DI cuando dependa de otras capacidades.

No crear cadenas artificiales como:

```text
Controller -> Manager -> Handler -> Processor -> Service -> Repository
```

si un simple:

```text
Controller -> Service
```

es suficiente.

### 5.4 Acceso a datos

Todo acceso SQL debe estar fuera de Controllers y componentes Razor.

Patrón esperado:

```text
Controller / Tool
       -> Service C#
            -> Microsoft.Data.SqlClient
                 -> SQL Server
```

Reglas:

- usar parámetros SQL;
- nunca concatenar input externo dentro del SQL;
- no duplicar una consulta ya existente si un servicio actual devuelve el mismo dato limpiamente;
- no exponer connection strings;
- no descubrir servidores o bases automáticamente;
- no acceder a otra base o servidor sólo porque sea técnicamente posible;
- lectura SQL es el comportamiento por defecto;
- cualquier escritura SQL requiere autorización explícita en el prompt actual.


### 5.4.1 SQL ejecutado vs. SQL generado

Distinguir siempre entre:

```text
SQL ejecutado por DesignerIA
```

y:

```text
script SQL generado como artefacto
```

Para SQL que DesignerIA ejecuta contra una base:

- usar parámetros SQL;
- nunca concatenar input externo;
- mantener lectura como comportamiento por defecto;
- cualquier escritura requiere autorización explícita en el prompt actual.

Para scripts SQL que DesignerIA genera pero **no ejecuta**:

- la salida debe construirse de forma determinística desde C# controlado;
- nunca insertar texto externo crudo dentro de literales SQL;
- todo valor textual debe pasar por el mecanismo de escape central definido por DesignerIA;
- no permitir que Copilot produzca SQL arbitrario como texto libre;
- generar un script no autoriza ejecutarlo.

### 5.4.2 Compatibilidad del SQL generado para GestionEngine

Todo script SQL generado por DesignerIA para GestionEngine debe ser compatible, como mínimo, con **Microsoft SQL Server 2008 R2**, salvo que el prompt actual autorice explícitamente un nivel de compatibilidad superior para un ambiente concreto.

La compatibilidad con SQL Server 2008 R2 forma parte de la corrección del script.

Un script no se considera válido para GestionEngine si únicamente funciona en una versión moderna de SQL Server cuando debe poder utilizarse en ambientes legacy.

Reglas permanentes:

- no asumir que la versión de SQL Server usada localmente representa la versión de los ambientes destino;
- no modernizar T-SQL por iniciativa propia;
- cuando existan varias formas equivalentes de expresar una operación, preferir la variante compatible con SQL Server 2008 R2;
- preferir patrones comprobados en scripts reales de Designer disponibles en `KnowledgeSource`;
- tratar configuraciones JSON como texto cuando el modelo de GestionEngine así lo haga;
- no introducir sintaxis posterior a SQL Server 2008 R2 salvo autorización explícita y evidencia de compatibilidad del ambiente destino;
- si existe duda sobre compatibilidad, mantener el script conservador o reportar la duda; no asumir soporte por haber compilado contra una instancia moderna.

Evitar por defecto construcciones posteriores a SQL Server 2008 R2, entre otras:

- `DROP TABLE IF EXISTS`;
- `THROW`;
- `TRY_CONVERT`;
- `IIF`;
- `CONCAT`;
- `OFFSET / FETCH`;
- `SEQUENCE`;
- `DATEFROMPARTS`;
- `EOMONTH`;
- `FORMAT`;
- `JSON_VALUE`;
- `OPENJSON`;
- `STRING_SPLIT`;
- `STRING_AGG`;
- `CREATE OR ALTER`.

Cuando corresponda y exista evidencia de dominio, preferir patrones clásicos compatibles, por ejemplo:

- `IF OBJECT_ID(...) IS NOT NULL` seguido de `DROP`;
- `BEGIN TRY` / `BEGIN CATCH`;
- `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK`;
- variables T-SQL tradicionales;
- `ISNULL`;
- `MAX(...)`;
- configuración serializada almacenada como texto.

### 5.4.3 Modelo lógico y ambiente

La definición lógica de una vista no debe acoplarse a detalles físicos de un ambiente.

Regla conceptual:

```text
ViewSpec = qué es la vista
Ambiente / contexto de generación = dónde y cómo se materializa
```

Por lo tanto:

- `ViewSpec` describe la estructura y comportamiento lógico de la vista;
- prefijos de metadata como `MTDE_`, acceso por tres capas, servidor, base, connection string física, versión de SQL Server y demás diferencias de ambiente no deben incorporarse al modelo lógico salvo que una necesidad real lo justifique;
- `prefijoModeloDatos` y el uso de three layers son conceptos independientes;
- no inferir uno a partir del otro;
- no inferir configuración de ambiente si no está explícitamente definida;
- mientras una tarea trabaje con un solo ambiente autorizado, no crear abstracciones multiambiente por anticipación;
- una futura abstracción de ambiente sólo debe introducirse cuando exista un requerimiento concreto.

### 5.5 GitHub Copilot SDK

Copilot es una capacidad del backend, no una puerta abierta al sistema.

Patrón esperado:

```text
Copilot
   -> custom tool explícita
        -> Service C# controlado
             -> capacidad autorizada
```

Reglas permanentes:

- no dar a Copilot acceso SQL genérico;
- no permitir que el modelo construya consultas arbitrarias para ejecutarlas;
- no habilitar shell, filesystem, Git, web, MCP, skills u otras tools por defecto;
- usar allowlists explícitas de tools;
- cada tool debe representar una capacidad pequeña y concreta;
- una tool no debe recibir parámetros que amplíen su autoridad innecesariamente;
- una tool read-only puede evitar confirmación sólo si su implementación está completamente controlada y no tiene efectos secundarios;
- no usar `PermissionHandler.ApproveAll` para simplificar;
- no pedir, copiar o guardar PATs, API keys, tokens o passwords;
- no ejecutar `copilot login` o `copilot logout` desde DesignerIA;
- reutilizar mecanismos oficiales de autenticación únicamente cuando la tarea los autorice.

---

## 6. Contracts

Los DTOs compartidos entre Web y Api deben vivir en:

```text
DesignerIA.Contracts
```

No duplicar el mismo contrato en Web y Api.

Los Contracts deben ser simples.

Deben contener únicamente datos que realmente crucen la frontera HTTP entre aplicaciones.

No colocar lógica de negocio dentro de Contracts.

---

## 7. Frontend

La interfaz debe ser:

- moderna;
- sobria;
- corporativa;
- limpia;
- responsive;
- consistente;
- preparada para branding futuro.

Tecnología:

```text
Blazor WebAssembly + Radzen.Blazor + CSS propio
```

Radzen es una librería de componentes, no la arquitectura visual completa.

Usar CSS propio y variables de diseño para mantener control sobre el look & feel.

Los textos visibles para el usuario deben estar en español.

Los componentes Razor deben concentrarse en:

- estado de UI;
- interacción del usuario;
- llamadas HTTP;
- presentación.

No colocar lógica de negocio, SQL ni lógica del SDK dentro de `.razor`.

Cuando una pantalla crezca, extraer responsabilidades sólo cuando exista una necesidad concreta.

### Dirección UX

DesignerIA debe evolucionar como un **workspace empresarial asistido por IA**, no como un chatbot genérico.

La dirección visual contempla progresivamente:

- navegación/historial lateral;
- área central de conversación;
- composer amplio;
- prompt starters;
- resultados estructurados mediante cards;
- panel de artifact para mostrar la vista que se está construyendo;
- estados visibles de API, datos e IA;
- acciones contextuales;
- progreso/streaming cuando corresponda;
- feedback del usuario.

Evitar:

- apariencia de formulario administrativo viejo;
- chatbot flotante como experiencia principal;
- exceso de modales;
- exceso de bordes;
- controles visuales inconsistentes;
- mostrar detalles técnicos innecesarios al usuario final.

Esta dirección UX no autoriza implementar features que el prompt actual no solicite.

---

## 8. Logging y observabilidad

Los logs son parte obligatoria de cualquier funcionalidad relevante.

Objetivo:

```text
si algo falla -> poder saber qué operación falló, dónde falló y por qué
```

Usar el sistema estándar de logging de ASP.NET Core:

```csharp
ILogger<T>
```

No agregar Serilog, Application Insights u otra dependencia de logging salvo autorización explícita.

Los mensajes técnicos de log deben escribirse en inglés.

La UI continúa en español.

### 8.1 Qué registrar

Registrar eventos significativos, especialmente en fronteras del sistema:

- inicio o final relevante de operaciones externas cuando aporte valor;
- llamadas a SQL;
- llamadas al GitHub Copilot SDK;
- invocación de custom tools;
- fallos de autenticación controlados;
- timeouts;
- excepciones;
- respuestas inesperadas de dependencias;
- operaciones cuyo diagnóstico posterior sería difícil sin contexto.

No registrar cada línea o cada método trivial.

### 8.2 Niveles

Usar niveles de forma consistente:

```text
Debug       -> detalle útil únicamente para diagnóstico local
Information -> evento normal y relevante del flujo
Warning     -> condición inesperada pero recuperable
Error       -> operación fallida
Critical    -> fallo grave de la aplicación, sólo cuando realmente corresponda
```

No convertir fallos normales/controlados en `Critical`.

### 8.3 Logs estructurados

Preferir templates estructurados:

```csharp
_logger.LogInformation(
    "Copilot metadata tool completed in {ElapsedMs} ms. ViewsCount: {ViewsCount}",
    elapsedMs,
    viewsCount);
```

Evitar:

```csharp
_logger.LogInformation($"Terminó todo y dio {viewsCount}");
```

Usar nombres de propiedades consistentes entre logs.

Cuando sea útil incluir:

- operación;
- duración;
- endpoint/capacidad;
- resultado;
- identificador seguro;
- conteos;
- tipo de error.

### 8.4 Errores

Cuando se capture una excepción relevante, registrar la excepción completa mediante `ILogger` para conservar stack trace:

```csharp
_logger.LogError(
    ex,
    "Failed to retrieve GestionEngine metadata");
```

La respuesta HTTP al Front debe seguir siendo controlada y no exponer el stack trace.

### 8.5 Información prohibida en logs

Nunca registrar:

- passwords;
- tokens;
- cookies;
- headers `Authorization`;
- connection strings completas;
- secretos;
- credenciales;
- prompts completos que puedan contener información sensible;
- respuestas completas de IA si pueden contener información interna innecesaria;
- datos sensibles sólo "por si luego sirven".

Registrar el mínimo contexto seguro necesario para diagnosticar.

### 8.6 Correlación

Usar primero las capacidades estándar de ASP.NET Core (`TraceIdentifier`, scopes o contexto HTTP) cuando se necesite correlacionar una operación.

No crear un framework propio de correlation IDs salvo necesidad real.

---

## 9. Manejo de errores

Los errores deben manejarse de forma consistente.

Reglas:

- no esconder excepciones silenciosamente;
- no usar `catch { }`;
- no convertir toda excepción en una respuesta genérica sin dejar evidencia en logs;
- no exponer stack traces o secretos al Front;
- diferenciar errores esperados de fallos inesperados;
- mantener mensajes de usuario sencillos;
- registrar detalles técnicos únicamente en backend.

No crear jerarquías complejas de excepciones personalizadas salvo necesidad real.

Si existe un patrón de manejo de errores ya establecido en DesignerIA, reutilizarlo.

---

## 10. Dependencias

Agregar una dependencia nueva es una decisión explícita.

El agente NO debe instalar paquetes por iniciativa propia.

Antes de agregar una dependencia no autorizada debe preguntar.

No modificar:

- `NuGet.Config` global;
- `NuGet.Config` local;
- fuentes globales de NuGet;
- herramientas globales .NET;
- configuración global de Visual Studio.

Para dependencias públicas autorizadas, usar `nuget.org` directamente cuando sea necesario.

No inspeccionar el caché global de paquetes para descubrir APIs.

Usar:

1. documentación oficial;
2. código existente del proyecto;
3. compilador;
4. IntelliSense/API pública accesible normalmente.

---

## 11. Seguridad

Nunca guardar o exponer:

- passwords;
- tokens;
- PATs;
- API keys;
- cookies;
- secrets;
- headers de autorización.

No colocar secretos en `wwwroot`.

No hardcodear credenciales.

No imprimir secretos en logs.

No ampliar permisos para "hacer que funcione".

Aplicar el principio:

```text
mínimo acceso necesario para la tarea actual
```

---

## 12. Git, Azure e infraestructura

El agente NO debe realizar operaciones Git sin autorización explícita.

Esto incluye:

- init;
- add;
- commit;
- push;
- pull;
- fetch;
- branch;
- merge;
- rebase;
- clone;
- Pull Requests;
- creación de repositorios.

El agente NO debe consultar, crear o modificar Azure sin autorización explícita.

Esto incluye:

- subscriptions;
- resource groups;
- Azure Repos;
- pipelines;
- VMs;
- App Services;
- Key Vault;
- Foundry;
- Azure OpenAI;
- Service Bus;
- Application Insights;
- deployments.

No Docker, Kubernetes, Redis u otra infraestructura no solicitada.

---

## 13. Qué puede asumir el agente

Cuando el usuario autoriza una tarea, el agente debe avanzar de forma autónoma dentro de su alcance.

Puede asumir autorización para:

- leer archivos dentro de `C:\Code\DesignerAI`;
- crear archivos necesarios dentro del proyecto;
- modificar archivos necesarios para la tarea;
- crear carpetas dentro del proyecto;
- agregar clases, DTOs, services o componentes necesarios;
- ejecutar `dotnet restore`;
- ejecutar `dotnet build`;
- ejecutar `dotnet run`;
- corregir errores causados por sus propios cambios;
- consultar endpoints locales de DesignerIA para validar;
- consultar documentación pública oficial de una dependencia ya autorizada cuando sea necesario;
- elegir la solución más simple entre alternativas técnicamente equivalentes.

No debe pedir confirmación para operaciones rutinarias que ya están implícitamente autorizadas por el prompt.

---

## 14. Cuándo debe preguntar

Debe detenerse y preguntar antes de:

- eliminar archivos o carpetas preexistentes;
- sobrescribir trabajo ajeno a la tarea;
- mover o renombrar elementos de forma destructiva;
- salir de `C:\Code\DesignerAI`;
- inspeccionar rutas personales/globales del sistema;
- agregar una dependencia no autorizada;
- cambiar significativamente la arquitectura;
- introducir un nuevo patrón para una responsabilidad ya resuelta;
- ampliar acceso SQL;
- ejecutar escritura SQL;
- acceder a otra base o servidor;
- tocar Git;
- tocar Azure;
- modificar infraestructura;
- cambiar autenticación;
- pedir o manipular credenciales;
- hacer deploy;
- realizar una acción con riesgo real de pérdida de información;
- ampliar el alcance funcional del prompt.

Si sólo existe una duda de implementación y puede resolverse leyendo el código actual, compilando o consultando documentación oficial, debe resolverla sin preguntar.

---

## 15. Operaciones prohibidas por defecto

Sin autorización explícita, no:

- eliminar archivos preexistentes;
- usar `Remove-Item` de forma amplia;
- inspeccionar otros proyectos;
- buscar ejemplos en repositorios de terceros;
- decompilar ensamblados;
- usar ILSpy;
- inspeccionar almacenes de credenciales;
- inspeccionar cachés globales de NuGet;
- ejecutar PowerShell para acceder directamente a SQL;
- usar `sqlcmd`;
- usar `Invoke-Sqlcmd`;
- automatizar SSMS;
- ejecutar Git;
- ejecutar Azure CLI;
- habilitar herramientas generales para Copilot;
- escribir en SQL;
- instalar herramientas globales;
- modificar configuraciones globales de Windows, .NET, NuGet, Git, Visual Studio, Copilot CLI o PowerShell.

---

## 16. Validación de una tarea

Antes de declarar una tarea terminada:

1. compilar la solución o los proyectos afectados;
2. corregir errores introducidos por la tarea;
3. validar el flujo modificado de la forma más pequeña posible;
4. verificar que funcionalidad relacionada existente no se haya roto cuando sea razonable;
5. no repetir llamadas costosas únicamente para "estar más seguro" si la primera validación fue concluyente;
6. resumir brevemente:
   - qué cambió;
   - qué se validó;
   - resultado;
   - cómo probarlo manualmente si aplica;
7. detenerse.

No continuar automáticamente con una nueva feature.

---

## 17. Regla de alcance

El prompt define la tarea.

Este archivo define la forma de trabajar.

Si el prompt pide una funcionalidad pequeña, implementar una funcionalidad pequeña.

No preparar automáticamente fases futuras.

No agregar código especulativo.

No refactorizar áreas no relacionadas "aprovechando que estamos aquí".

No cambiar tecnologías o patrones sin necesidad.

---

## 18. Regla principal

Ante dos soluciones correctas, elegir la que sea:

```text
más simple
+ más clara
+ consistente con DesignerIA
+ fácil de diagnosticar
+ segura
+ suficiente para el requerimiento actual
```

DesignerIA debe crecer paso a paso manteniendo una base de código que otro desarrollador pueda leer y entender sin necesitar explicaciones especiales.
