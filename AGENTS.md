# DesignerIA — AGENTS.md

## 1. Propósito

Este archivo define **cómo debe trabajar cualquier agente de desarrollo dentro de DesignerIA**.

No describe una tarea puntual. Las features, endpoints, pruebas, contratos y alcance funcional llegan en el **prompt de trabajo**.

```text
AGENTS.md = cómo trabajar siempre
Prompt     = qué hacer ahora
```

El agente debe cumplir ambos.

---

## 2. Ruta autorizada y fuentes de verdad

Ruta autorizada:

```text
C:\Code\DesignerAI
```

Fuentes de verdad, en este orden:

1. instrucción actual del usuario;
2. este `AGENTS.md`;
3. código/configuración dentro de `C:\Code\DesignerAI`;
4. evidencia autorizada dentro de `C:\Code\DesignerAI\KnowledgeSource`.

No inspeccionar ni reutilizar código de otros proyectos locales o repositorios externos para ahorrar trabajo.

No usar directamente como referencia interna:

- Lumed;
- Portal;
- ViewDesignerSolution;
- Designer;
- Motor;
- otros proyectos bajo `C:\Code`;
- GitHub/Azure Repos;
- proyectos de terceros.

Puede consultarse documentación pública oficial de dependencias ya autorizadas.

No decompilar DLLs, usar ILSpy, reflexión exploratoria ni inspeccionar cachés globales de NuGet.

---

## 3. `KnowledgeSource`

`C:\Code\DesignerAI\KnowledgeSource` puede contener Wiki, scripts SQL, ejemplos reales, documentación y copias aisladas de código usadas únicamente como **evidencia de dominio**.

Reglas:

- se puede leer y analizar;
- no es código productivo;
- no compilarlo ni referenciarlo desde runtime;
- no copiar código viejo mecánicamente;
- extraer reglas y reimplementarlas en DesignerIA;
- preferir evidencia directa de implementación/artefactos reales sobre documentación interpretativa cuando se contradigan;
- documentar contradicciones;
- no inventar reglas para resolver huecos;
- no publicarlo en Git.

---

## 4. Stack y arquitectura

```text
DesignerIA.slnx
├── DesignerIA.Web
├── DesignerIA.Api
└── DesignerIA.Contracts
```

Stack:

- .NET 10;
- C#;
- Blazor WebAssembly;
- ASP.NET Core Web API;
- Radzen.Blazor;
- CSS propio;
- `Microsoft.Data.SqlClient`;
- GitHub Copilot SDK cuando aplique.

Dependencias:

```text
DesignerIA.Web       -> DesignerIA.Contracts
DesignerIA.Api       -> DesignerIA.Contracts
DesignerIA.Web       -X-> DesignerIA.Api como referencia de proyecto
DesignerIA.Web       -> DesignerIA.Api sólo por HTTP
```

Reglas:

- Web nunca se conecta directo a SQL;
- Web nunca usa directo el Copilot SDK;
- SQL y Copilot viven en API;
- DTOs compartidos viven en Contracts;
- no crear proyectos ni cambiar arquitectura sin autorización explícita.

---

## 5. Filosofía de trabajo

Preferir la solución más simple que resuelva correctamente el problema.

**Una tarea pequeña debe producir un cambio pequeño.**

El código debe ser:

- simple;
- explícito;
- legible;
- mantenible;
- predecible;
- consistente;
- fácil de depurar;
- suficiente para el requerimiento actual.

Preferir **código aburrido y evidente** sobre código ingenioso.

Más código no significa una solución más profesional.

Antes de crear algo nuevo, revisar cómo DesignerIA resuelve una responsabilidad equivalente.

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
- reflexión/metaprogramación;
- service locator;
- helpers genéricos sin necesidad;
- abstracciones "por si acaso";
- refactors no solicitados;
- cambios cosméticos ajenos;
- generalizaciones prematuras.

Escalable no significa agregar capas anticipadamente.

---

## 6. Evidencia antes que suposición

Antes de modificar:

1. localizar el flujo actual;
2. identificar archivos/símbolos involucrados;
3. revisar casos equivalentes;
4. rastrear el dato entre capas;
5. revisar `KnowledgeSource` cuando aplique;
6. distinguir hechos de hipótesis.

No asumir que una funcionalidad existe porque aparece un parámetro, columna, componente, tabla, SP, DTO o nombre parecido.

Clasificar hallazgos como:

- confirmado;
- inferido;
- hipótesis;
- pendiente;
- falta evidencia;
- bloqueado por falta de evidencia.

No convertir hipótesis en decisiones arquitectónicas.

Si código, SQL, configuración, documentación o evidencia se contradicen, reportarlo. No corregirlo silenciosamente.

---

## 7. Modos de trabajo

### 7.1 Análisis — READ-ONLY

Cuando el usuario diga `analiza`, `revisa`, `investiga`, `diagnostica`, `inspecciona`, `compara` o equivalente, trabajar READ-ONLY.

Puede hacer automáticamente:

- leer archivos;
- listar carpetas;
- buscar referencias;
- inspeccionar código/configuración/SQL local;
- comparar archivos;
- Git de lectura;
- documentación pública oficial autorizada;
- SQL READ-ONLY sólo si servidor, base y autenticación fueron autorizados para esa tarea.

No pedir autorización para lecturas rutinarias.

Durante análisis no:

- modificar archivos;
- generar artefactos dentro del workspace;
- hacer restore/build;
- instalar dependencias;
- ejecutar SQL de escritura;
- ejecutar SP funcionales;
- modificar Git;
- tocar Azure;
- desplegar.

Una solicitud de análisis **no autoriza implementación**.

### 7.2 Autonomía READ-ONLY

Regla:

> Si una operación no modifica estado, está dentro del alcance autorizado y sirve a la tarea, ejecutarla sin preguntar.

Incluye:

```text
git status
git status --short
git diff
git diff --check
git log
git show
git branch --show-current
git remote -v
```

y, cuando SQL READ-ONLY esté autorizado:

```text
SELECT
TOP
COUNT
INFORMATION_SCHEMA
sys.*
OBJECT_DEFINITION
sys.sql_modules
```

No preguntar "¿puedo leer/buscar/ejecutar git status/ejecutar este SELECT?" si ya está autorizado.

**La herramienta no determina el riesgo. El efecto de la operación sí.**

### 7.3 Implementación

Cuando el usuario diga `implementa`, `hazlo`, `corrige`, `ajusta`, `agrega` o `modifica`, puede editar lo necesario dentro del alcance.

Puede:

- crear/modificar archivos necesarios;
- crear carpetas;
- agregar clases/DTOs/Services/componentes;
- `dotnet restore` si no agrega dependencias nuevas;
- `dotnet build`;
- `dotnet run`;
- probar endpoints locales;
- corregir errores introducidos por sus propios cambios.

No pedir autorización por cada edición rutinaria.

No aprovechar para:

- refactorizar módulos vecinos;
- renombrar cosas no relacionadas;
- modernizar código;
- corregir warnings históricos;
- eliminar código aparentemente muerto;
- cambiar estilos no solicitados;
- introducir patrones nuevos sin necesidad.

Una implementación no autoriza Git de escritura, Azure, deploy, SQL de escritura, dependencias nuevas ni cambios de autenticación.

---

## 8. Estándares C#

- `PascalCase` para tipos/miembros públicos;
- `camelCase` para locales/parámetros;
- nombres descriptivos;
- métodos async terminan en `Async`;
- usar `async/await` para I/O;
- propagar `CancellationToken` cuando tenga sentido;
- evitar clases/métodos gigantes;
- no duplicar lógica;
- no crear interfaces por costumbre.

Crear interfaz sólo si existe razón real: varias implementaciones, contrato reutilizable, desacoplamiento claro o estrategia de pruebas que lo justifique.

### Controllers

Deben ser delgados:

```text
HTTP request
 -> validación de transporte mínima
 -> Service
 -> HTTP response
```

No SQL, Copilot SDK, reglas de dominio ni lógica compleja en Controllers.

### Services

Un Service debe tener responsabilidad clara y específica.

Evitar:

```text
Controller -> Manager -> Handler -> Processor -> Service -> Repository
```

si basta:

```text
Controller -> Service
```

---

## 9. SQL y acceso a datos

Patrón:

```text
Controller / Tool
 -> Service C#
 -> Microsoft.Data.SqlClient
 -> SQL Server
```

Reglas:

- parámetros SQL para SQL ejecutado;
- nunca concatenar input externo;
- no exponer connection strings;
- no descubrir servidores/bases automáticamente;
- lectura es el default;
- escritura SQL requiere autorización explícita.

### SQL ejecutado vs SQL generado

Distinguir siempre:

```text
SQL ejecutado por DesignerIA
```

de:

```text
script SQL generado como artefacto
```

Para scripts generados:

- generación determinística en C#;
- texto externo siempre escapado;
- mecanismo central de escape;
- Copilot nunca genera SQL arbitrario libre;
- generar script no autoriza ejecutarlo.

### Compatibilidad GestionEngine

Todo SQL generado debe ser compatible, como mínimo, con **SQL Server 2008 R2** salvo autorización explícita distinta.

Evitar por defecto:

- `DROP TABLE IF EXISTS`;
- `THROW`;
- `TRY_CONVERT`;
- `IIF`;
- `CONCAT`;
- `OFFSET/FETCH`;
- `SEQUENCE`;
- `DATEFROMPARTS`;
- `EOMONTH`;
- `FORMAT`;
- `JSON_VALUE`;
- `OPENJSON`;
- `STRING_SPLIT`;
- `STRING_AGG`;
- `CREATE OR ALTER`.

Preferir patrones clásicos comprobados en evidencia real.

### Modelo lógico vs ambiente

```text
ViewSpec = qué es la vista
Ambiente = dónde/cómo se materializa
```

No meter servidor, base, connection string o versión SQL dentro del modelo lógico sin necesidad real.

No crear multiambiente por anticipación.

---

## 10. GitHub Copilot SDK

Copilot es capacidad del backend, no acceso general al sistema.

```text
Copilot
 -> custom tool explícita
 -> Service C# controlado
 -> capacidad autorizada
```

Reglas:

- no SQL genérico;
- no consultas arbitrarias ejecutables;
- no shell/filesystem/Git/web/MCP/skills por defecto;
- allowlist explícita de tools;
- tools pequeñas y concretas;
- no `PermissionHandler.ApproveAll`;
- no pedir/guardar PAT, keys, tokens o passwords;
- no `copilot login/logout` desde DesignerIA.

La semántica puede ir al modelo.

Reglas, validaciones, autorización, SQL, seguridad y efectos secundarios deben quedar en C# determinístico.

---

## 11. Contracts

DTOs compartidos:

```text
DesignerIA.Contracts
```

No duplicarlos en Web/API.

Contracts simples, sólo datos que crucen HTTP.

Sin lógica de negocio.

---

## 12. Frontend

Debe ser moderno, sobrio, corporativo, limpio, responsive y consistente.

Tecnología:

```text
Blazor WebAssembly + Radzen.Blazor + CSS propio
```

Razor se concentra en UI, interacción, llamadas HTTP y presentación.

No lógica de negocio, SQL ni SDK en `.razor`.

Dirección UX: workspace empresarial asistido por IA, no chatbot genérico.

Evitar modales/bordes excesivos, inconsistencia visual y detalles técnicos innecesarios.

---

## 13. Logging

Usar `ILogger<T>`.

Logs técnicos en inglés; UI en español.

Registrar eventos significativos: SQL, Copilot, tools, auth, timeouts, excepciones y respuestas inesperadas.

Preferir logs estructurados.

Nunca registrar:

- passwords;
- tokens;
- cookies;
- `Authorization`;
- connection strings completas;
- secretos;
- prompts/respuestas completas sensibles.

---

## 14. Errores

- no `catch { }`;
- no esconder excepciones;
- no exponer stack traces al Front;
- distinguir error esperado de fallo inesperado;
- mensaje simple al usuario;
- detalle técnico en backend;
- reutilizar patrón existente.

---

## 15. Dependencias/configuración

Dependencia nueva = decisión explícita.

No instalar paquetes por iniciativa propia.

No modificar configuración global de NuGet, .NET, Git, Visual Studio, VS Code, PowerShell, certificados o variables de ambiente.

Usar documentación oficial, código actual, compilador e IntelliSense.

---

## 16. Seguridad

Nunca guardar/exponer:

- passwords;
- tokens;
- PATs;
- API keys;
- cookies;
- secrets;
- headers de autorización;
- claves privadas.

No secretos en `wwwroot`.

No hardcodear credenciales.

Principio:

```text
mínimo acceso necesario
```

Con Windows Authentication usar la identidad actual sin intentar extraer credenciales.

---

## 17. Git — frontera estricta

### Lectura permitida automáticamente

```text
git status
git status --short
git diff
git diff --check
git log
git show
git branch --show-current
git remote -v
```

### Escritura requiere autorización explícita actual

No ejecutar por iniciativa propia:

- `git init`;
- `git add`;
- `git commit`;
- `git push`;
- `git pull`;
- `git fetch`;
- `git switch/checkout`;
- `git restore`;
- `git clean`;
- `git rm`;
- crear/cambiar/borrar ramas;
- merge/rebase/reset/cherry-pick/revert/stash;
- tags;
- configuración Git;
- PRs;
- repos remotos.

Implementar código no autoriza Git.

Antes de implementar, revisar `git status --short`.

Preservar cambios preexistentes: no revertir, descartar, sobrescribir ni stash.

Mantener diff mínimo y preservar formato/EOL.

### Modelo de ramas autorizado cuando corresponda

```text
main       -> estable / producción
develop    -> integración siguiente versión
feature/*  -> trabajo puntual
release/*  -> candidato a staging/release
hotfix/*   -> corrección urgente desde main
```

Ejemplos:

```text
feature/chat-markdown
feature/copilot-model-config
feature/create-view-executor
release/v1.1.0
hotfix/auth-null-user
```

No mezclar conceptos como `Feature/Release/...`.

`main` no recibe desarrollo directo.

Preferir `release/*` como candidato a staging en lugar de rama eterna `staging`, salvo decisión explícita distinta.

---

## 18. Azure/infraestructura/deploy

Todo requiere autorización explícita.

No tocar por iniciativa propia:

- Azure Repos;
- pipelines;
- VMs;
- App Services;
- Key Vault;
- Azure OpenAI/Foundry;
- Service Bus;
- Application Insights;
- environments;
- service connections;
- deployments;
- Docker/Kubernetes/Redis.

Git autorizado no implica Azure autorizado.

---

## 19. SQL — efectos secundarios

Con servidor/base/auth autorizados, SQL READ-ONLY puede ejecutarse sin preguntar por cada consulta.

Preferir metadata, muestras y agregaciones.

No ejecutar automáticamente:

- `INSERT`;
- `UPDATE`;
- `DELETE`;
- `TRUNCATE`;
- `MERGE`;
- `CREATE`;
- `ALTER`;
- `DROP`;
- `SELECT INTO`;
- SP funcionales;
- cargas;
- backups/restores;
- Jobs;
- permisos/usuarios/roles/configuración.

No asumir que un SP es read-only por su nombre.

Ante duda: inspeccionar, no ejecutar.

---

## 20. Cuándo preguntar

Preguntar antes de:

- borrar archivos preexistentes;
- sobrescribir trabajo ajeno;
- mover/renombrar destructivamente;
- salir de `C:\Code\DesignerAI`;
- agregar dependencia;
- cambiar arquitectura significativamente;
- introducir patrón nuevo;
- ampliar SQL;
- SQL de escritura;
- otro servidor/base;
- Git de escritura;
- Azure;
- infraestructura;
- autenticación;
- credenciales;
- deploy;
- riesgo real de pérdida;
- ampliar alcance;
- decisión funcional/arquitectónica sin evidencia suficiente.

Si una duda puede resolverse leyendo código/evidencia/configuración, compilando o con READ-ONLY autorizado, resolverla sin interrumpir.

> **Libertad para trabajar, no libertad para publicar ni destruir.**

---

## 21. Validación proporcional

Antes de declarar una implementación terminada:

1. compilar proyectos afectados;
2. corregir errores introducidos;
3. validar el flujo de la forma más pequeña posible;
4. verificar funcionalidad relacionada cuando sea razonable;
5. no repetir llamadas costosas sin necesidad;
6. reportar qué cambió;
7. reportar qué se validó;
8. reportar qué NO se validó;
9. indicar prueba manual si aplica;
10. detenerse.

No confundir:

```text
compiló != funciona en runtime
inspeccionado != probado
SQL generado != SQL ejecutado
SQL ejecutado != Motor/Designer lo interpreta
```

No afirmar más de lo validado.

---

## 22. Reporte

### Análisis

Reportar:

- flujo confirmado;
- evidencia;
- archivos/símbolos;
- tablas/SP si aplica;
- hipótesis;
- falta de evidencia;
- contradicciones;
- impacto;
- propuesta mínima;
- qué NO tocar.

### Implementación

Reportar:

- archivos modificados;
- motivo;
- validaciones realizadas;
- validaciones no realizadas;
- resultado;
- riesgos;
- pendientes;
- cambios preexistentes preservados.

No continuar automáticamente con otra feature.

---

## 23. Regla de alcance

El prompt define la tarea.

Este archivo define la forma de trabajar.

No preparar fases futuras, agregar código especulativo, refactorizar áreas no relacionadas ni cambiar tecnologías/patrones sin necesidad.

---

## 24. Checklist mental

Antes de actuar:

1. ¿Forma parte de la tarea?
2. ¿Tengo evidencia?
3. ¿Existe un patrón similar?
4. ¿Puedo hacerlo con menos cambios?
5. ¿Preservo cambios existentes?
6. ¿Toco Git/Azure/SQL/dependencias/auth/configuración sensible?
7. ¿Agrego complejidad no pedida?
8. ¿Estoy suponiendo algo comprobable?
9. ¿La acción tiene efectos secundarios?
10. ¿Tengo autorización para esa frontera?

---

## 25. Regla principal

Ante dos soluciones correctas, elegir la:

```text
más simple
+ más clara
+ consistente con DesignerIA
+ fácil de diagnosticar
+ segura
+ suficiente
```

Si 10 líneas resuelven correctamente el problema, no crear 100.

Implementar lo necesario.

Validar lo suficiente.

Reportar claramente.

Detenerse.
