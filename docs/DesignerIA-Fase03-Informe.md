# DesignerIA — Fase 3: Informe de cierre

Nota de publicacion: los identificadores de infraestructura y cuentas de Windows se sustituyeron por marcadores; el documento original se conserva en el equipo de desarrollo.

Fecha: 10 de octubre de 2026.

Referencias: [Contexto](DesignerIA-Contexto.md), [Roadmap](DesignerIA-Roadmap.md), [Contrato de fase 2](DesignerIA-Fase02-Contrato.md), [Plan de fase 3](DesignerIA-Fase03-Plan.md) y [Checkpoint de fase 4](DesignerIA-Fase04-Checkpoint.md).

## Estado de este documento

Registra qué se implementó en la fase 3, qué se comprobó y qué quedó sin comprobar. El contrato de fase 2 no cambió; las precisiones adoptadas durante la implementación están en la sección 3.

**La fase 3 quedó cerrada funcionalmente en el ambiente local.** El formulario de la plantilla «Consulta con grilla» produce el script completo de una vista nueva, o un rechazo sin SQL parcial.

**El script no se ejecutó.** No hubo SQL de escritura, no se ejecutó ningún Stored Procedure y no se comprobó nada en Motor. Eso corresponde a la fase 4.

El plan y el checkpoint de inicio de fase 3 se conservan como registro fechado: sus frases «no iniciada» describen el momento en que se escribieron.

## 1. Qué se entregó

Rama `feature/grid-query-generation`, creada desde el cierre de fase 2 (`bd36385`). Este informe se redactó antes de los commits de cierre de fase 3. Una vez hechos, el `HEAD` de la rama debe ser el commit de cierre documental, precedido por el de implementación; comprobarlo con Git de lectura.

| Paso del plan | Resultado |
|---|---|
| 0. Vía de compilación y pruebas | Comprobada: Claudio compila y prueba desde su sesión con `--artifacts-path C:\Code\DesignerAI\artifacts` |
| 1. Caracterización | `ViewScriptGenerationTests.cs` describió la vista mínima vacía antes de tocar el generador |
| 2. Contracts | Campos opcionales al final de `ViewSpec`, fila y área; `GridQueryTemplateRequest`; resultado de preflight con huecos, SP canónico y comprobaciones superadas |
| 3. Plantilla y preset | `GridConfigurationPresets` (constante `grid-consulta-v1`) y `GridQueryTemplate` |
| 4. Normalización | `ViewSpecNormalizer` conserva `ViewType` y no inventa nombres |
| 5. Validaciones | Comparación única de estados, estados duplicados como error, dos formas generables, longitudes y forma del SP |
| 6. Preflight | Seis comprobaciones por lectura de metadata; mapeo `ConnectionAliases` en `appsettings.json` |
| 7. Plan físico | Tipo de vista y parte opcional de grilla |
| 8. Generador | Una sola entrada pública; diez INSERT; estructura transaccional del contrato; literales Unicode y ANSI |
| 9. Controllers | Camino único en `POST api/view-script/generate`; nuevo `POST api/view-templates/grid-query` |
| 10. UI | «Nueva vista», galería, formulario, resumen ampliado y limpieza del script en `Home.razor` y `app.css` |
| 11. Verificación | Compilación, pruebas, HTTP, metadata por lectura y revisión visual (sección 4) |

Archivos nuevos:

- `DesignerIA.Contracts/GridQueryTemplateRequest.cs`
- `DesignerIA.Api/Controllers/ViewTemplateController.cs`
- `DesignerIA.Api/Services/ViewScripts/GridConfigurationPresets.cs`
- `DesignerIA.Api/Services/ViewTemplates/GridQueryTemplate.cs`
- `DesignerIA.Api.Tests/ViewScriptGenerationTests.cs`

Archivos modificados:

- Contracts: `ViewSpec.cs`, `ViewCreationPreflightResult.cs`.
- API: `ViewScriptController.cs`, `StringConnectionCatalogService.cs`, `ViewSpecNormalizer.cs`, `ViewSpecValidator.cs`, `SqlLiteralEscaper.cs`, `ViewCreationPlan.cs`, `ViewCreationPlanFactory.cs`, `ViewCreationPreflightService.cs`, `ViewScriptGenerationValidator.cs`, `ViewScriptGenerator.cs` y `appsettings.json`.
- Web: `Pages/Home.razor` y `wwwroot/css/app.css`.

No se agregaron proyectos, dependencias ni capas. `Program.cs`, el prompt y el esquema de Copilot no cambiaron.

## 2. Defectos del código anterior corregidos

Son los de la sección 8 del contrato:

- El endpoint de generación ya no construye el plan sin pasar por la validación de generación: una forma no soportada o un ViewSpec con datos pendientes se rechaza sin SQL parcial. Una grilla con la forma de la plantilla sí se genera.
- El estado inicial se compara con una sola regla en validador y factoría; los estados duplicados son un error.
- El normalizador conserva `ViewType`.
- El catálogo de alias distingue alias inexistente, ambiguo y Oracle.
- El generador usa el tipo de vista del plan, escribe literales `N` y comprueba la colisión dentro de la transacción.

Corrección acotada adicional, autorizada durante la fase: el panel ya no conserva el script de una vista anterior. El script se limpia al sustituir o reiniciar la vista, y una respuesta tardía de generación se descarta si la vista en progreso cambió.

## 3. Precisiones adoptadas durante la implementación

Aprobadas en revisión. No modifican el contrato; fijan detalles que el contrato no definía.

| Precisión | Decisión |
|---|---|
| Comparación de nombres de estado | `OrdinalIgnoreCase`; el plan usa el nombre tal como está en el estado |
| `ViewType` por forma | Vista vacía: nulo o `site`. Grilla: `go`. Otro valor se rechaza como forma no soportada |
| Clave de configuración | `ConnectionAliases` en `appsettings.json`, con `GestionEngine` → `GestionEngineLocal`; sin cadenas de conexión |
| `isOracle` nulo | Equivale a cero, como en los procedimientos del propio GestionEngine (`ISNULL(isOracle, 0)`); misma regla en preflight y resolver de alias |
| Preflight de una grilla | Exige las seis comprobaciones y el SP canónico. La disponibilidad del nombre por sí sola no habilita la generación |
| `CompletedChecks` | Campo opcional del resultado de preflight que acredita las comprobaciones superadas, con números de comprobación y sin IDs físicos |
| Preflight de otra vista | Se rechaza como «Preflight incompleto» sin trasladar sus rechazos |
| Rechazo concreto | Se conserva sin añadir «Preflight incompleto» por la misma causa |
| `Vistas.JsCode` | Es `varchar(max)` y recibe `N''`. Se mantiene en esta entrega por ser una cadena vacía |
| Asignación de IDs | La vista vacía conserva lectura de `MAX` e incremento con `SET`; la grilla suma en la lectura, como en la sección 5 del contrato |

`StringConnectionCatalogService.ResolveIdByAliasAsync` se conservó aunque no tiene llamadores.

## 4. Comprobaciones realizadas

### Compilación y pruebas

Ejecutadas por Claudio desde su sesión, con salida en `artifacts/` (carpeta ignorada por Git):

```text
dotnet build DesignerIA.slnx --artifacts-path C:\Code\DesignerAI\artifacts
dotnet test DesignerIA.slnx --no-build --artifacts-path C:\Code\DesignerAI\artifacts
```

- Compilación: 0 errores. Las advertencias son las históricas (`NU1900`, `NU1510` y tres `CS8602` en extractores de conocimiento) y no se corrigieron.
- Pruebas: **649 superadas, 0 con error, 0 omitidas.** Son las 570 anteriores más 79 casos de generación.
- Las pruebas de generación no usan SQL, `KnowledgeSource` ni el corpus local. Por lectura del proyecto de pruebas, ninguna prueba de la solución llama a Copilot ni abre conexiones SQL.
- Las pruebas del corpus, anteriores a esta fase, leen archivos locales que no están en Git (`DesignerIA.Api/Data/Knowledge` y `App_Data/CuratedKnowledge`); en un clon limpio fallarían.

Las pruebas de generación cubren: la vista vacía antes y después de los siete cambios intencionales, los diez INSERT con sus columnas, valores, relaciones y auditoría, el preset por longitud y huella SHA-256, literales con comillas y acentos, formas no generables, forma del SP, longitudes, estados duplicados, `ViewType` tras normalizar y serializar, la plantilla contra el ejemplo del contrato, cada rechazo del preflight, el preflight incompleto y la ausencia de construcciones vetadas para SQL Server 2008 R2.

### HTTP

API local en `https://localhost:7089`, con autenticación Windows y lectura de metadata en `SQL_LOCAL_HOST / GestionEngine`.

| Caso | Resultado observado |
|---|---|
| Petición sin credenciales | 401 |
| `POST api/view-templates/grid-query` con `dbo.PRC_GET_GRID_TEST_SORT_COLUMN` y alias `GestionEngine` | 200; ViewSpec `go` con `Estado01`, `Fila01`, `Area01`, `Grilla01` y preset; validación sin errores |
| `POST api/view-script/generate` con ese ViewSpec | 200; `canGenerate = true`; script con los diez INSERT en orden |
| Preflight de ese caso | Seis comprobaciones superadas; SP canónico `[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]` |
| SP escrito como `[DBO].[prc_get_grid_test_sort_column]` | Script generado con la capitalización del catálogo |
| Vista mínima vacía | Script con sus cuatro INSERT |

Rechazos observados, todos con `canGenerate = false` y `script = null`:

| Caso | Rechazo |
|---|---|
| Nombre existente, también con otra capitalización | «Nombre de vista existente» |
| Nombre vacío | La plantilla devuelve «La vista no tiene nombre.»; la generación responde «ViewSpec inválido» sin ejecutar el preflight |
| SP inexistente | «SP inexistente o no visible para DesignerIA» |
| SP sin esquema | «Forma del SP no admitida», sin ejecutar el preflight |
| Alias inexistente | «Alias inexistente» y «Alias sin conexión configurada» |

La colisión se probó con una vista existente en la base local. `vpruebaJFAL`, la vista del export de referencia, no existe en esa base.

### Metadata por lectura

Consultas `SELECT` en `SQL_LOCAL_HOST / GestionEngine`, con autenticación Windows:

- El alias `GestionEngine` tiene una fila con `isOracle = 0`.
- `dbo.Vistas` tenía 16 filas antes y después de las pruebas, y ninguna con los nombres usados en la demostración.

El preflight de la aplicación sólo ejecutó consultas de lectura. No se ejecutó el Stored Procedure.

### Revisión visual

Web local en `https://localhost:7276`, contra la API anterior.

- «+ Nueva vista» abre la galería con la tarjeta «Consulta con grilla» y después el formulario de cuatro campos, con SP y alias precargados y editables. Al reabrir, el formulario vuelve a su estado inicial.
- El resumen muestra tipo de vista, fila, área, objeto, SP, alias y preset.
- La generación muestra «Script generado. No ejecutado.» con el script completo. «Crear vista» sigue deshabilitado.
- Los rechazos por colisión y por SP inexistente muestran su motivo y ningún script. Con nombre vacío, «Generar script» queda deshabilitado.
- Al preparar otra vista y al pulsar «Nueva conversación», el script o rechazo anterior desaparece.
- Respuesta tardía: se retrasó artificialmente en el navegador la respuesta de generación de una vista y se preparó otra mientras tanto. Al llegar la respuesta no se mostró ningún script y el botón volvió a habilitarse.
- Sin errores en la consola del navegador ni en los logs de la API.

Las capturas, el script generado y las respuestas HTTP quedaron como evidencia local fuera de Git.

## 5. Cómo levantar la demo local

Requiere los puertos 7089 y 7276 libres, la base local accesible con la identidad de Windows y haber compilado antes con `--artifacts-path`. En dos terminales, desde la raíz del repositorio:

```text
dotnet run --project DesignerIA.Api --launch-profile https --no-build --artifacts-path C:\Code\DesignerAI\artifacts
dotnet run --project DesignerIA.Web --launch-profile https --no-build --artifacts-path C:\Code\DesignerAI\artifacts
```

Abrir `https://localhost:7276` y pulsar «+ Nueva vista». La Web llama a la API en `https://localhost:7089`, y la API sólo admite CORS desde `https://localhost:7276`. También funciona arrancando ambos proyectos desde Visual Studio con el perfil `https`.

## 6. Límites de lo comprobado

- **Limpieza del script en el flujo de chat:** revisada sólo por lectura. Ejercitarla requiere una llamada a Copilot.
- **Rechazos cubiertos sólo por pruebas unitarias:** «Alias ambiguo», «Alias no es SQL Server», «SP con parámetros no admitidos por la plantilla», «Resultado incompatible» y «Resultado no determinable». No hubo un caso real en la base local.
- **Script no ejecutado:** no se aplicó, no se comprobaron sus diez registros en la base ni su comportamiento transaccional.
- **SQL Server 2008 R2:** la compatibilidad está revisada por texto y por la prueba de construcciones vetadas. La base local es SQL Server 2022.
- **Motor:** pendiente. No se comprobó el render de la grilla, `UnidadAltoFila`, `@QueryString` sin mapeo ni `UsaSeguridad = 0`.
- **Designer:** no se comprobó abrir y guardar la vista.

Todas las comprobaciones de la sección 10 del contrato distintas de la generación y sus pruebas automáticas siguen pendientes.

## 7. Observaciones menores de presentación

Registradas sin corregir:

1. Un alias inexistente produce dos rechazos, «Alias inexistente» y «Alias sin conexión configurada». Ambos son ciertos, porque las comprobaciones 2 y 3 se evalúan siempre, pero es una sola causa para el usuario.
2. Con el nombre vacío, el título del resumen queda en blanco en lugar de «(vista sin nombre)», porque el nombre llega vacío y no nulo.

## 8. Forma de trabajo

- El ejecutor Codex escribió los archivos por entregas; su terminal no funcionó en este equipo, así que no ejecutó Git, compilación ni pruebas.
- Claudio comprobó el estado de Git antes y después de cada entrega, compiló, ejecutó las pruebas y revisó cada diff.
- El revisor aprobó cada entrega y las correcciones antes de continuar.

## Criterio de cierre de fase 3

El formulario produce el script completo de una vista nueva o un rechazo sin SQL parcial, con pruebas de relaciones, preset literal, Unicode y regresión de la vista vacía, y con comprobación HTTP y visual reportadas. No equivale a metadata aplicada ni a funcionamiento en Motor.
