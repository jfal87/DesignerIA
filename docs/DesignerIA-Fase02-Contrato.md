# DesignerIA — Fase 2: Plantilla y contrato

Fecha: 10 de octubre de 2026.

Referencias: [Contexto](DesignerIA-Contexto.md), [Roadmap](DesignerIA-Roadmap.md), [Fase 1](DesignerIA-Fase01-Alcance.md) y [Plan de fase 3](DesignerIA-Fase03-Plan.md).

## Estado de este documento

Es el contrato técnico revisado de la plantilla «Consulta con grilla». Define campos, defaults, nombres, componentes, relaciones, validaciones y la forma del script.

**Es una especificación revisada, no código.** Nada de lo descrito está implementado ni probado en runtime. No se generó ni aplicó ningún script, no se ejecutó ningún Stored Procedure y no se comprobó nada en Motor. Las comprobaciones pendientes están en la sección 10.

## 1. Contrato ViewSpec

MetaJSON es `ViewSpec` representado en JSON. Se amplía con campos opcionales al final de cada registro, para no romper los constructores existentes ni el esquema que hoy se entrega a Copilot:

```csharp
record ViewSpec(Name, Description, InitialState, States, string? ViewType = null);
record ViewSpecRow(Areas, string? Name = null);
record ViewSpecArea(Type, StoredProcedure, ConnectionAlias, Handlers,
    string? Name = null, string? ObjectName = null, string? ConfigurationPreset = null);
```

- `ViewType` nulo equivale a `site`, que es el comportamiento actual del generador y el default de Designer. La plantilla usa `go`.
- La plantilla fija todos los nombres. El normalizador conserva los campos nuevos y no inventa nombres de fila, área ni objeto.
- El contrato describe estructura y configuración. Los IDs físicos, servidores y conexiones pertenecen al ambiente y se resuelven en el backend.

Ejemplo completo de la primera grilla:

```json
{
  "name": "vConsultaPrueba01",
  "description": "Consulta de prueba con grilla",
  "viewType": "go",
  "initialState": "Estado01",
  "states": [{
    "name": "Estado01",
    "rows": [{
      "name": "Fila01",
      "areas": [{
        "name": "Area01",
        "objectName": "Grilla01",
        "type": "Grilla",
        "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN",
        "connectionAlias": "GestionEngine",
        "configurationPreset": "grid-consulta-v1",
        "handlers": []
      }]
    }]
  }]
}
```

### Preset de configuración

`grid-consulta-v1` es una constante única e inmutable en la API. Su valor es el texto exacto de la configuración del objeto `area_9728_Obj` en `KnowledgeSource/vpruebaJFAL.sql` (línea 112), incluidos sus espacios irregulares al final, sin normalizar.

| Dato | Valor |
|---|---|
| Longitud | 900 caracteres |
| Claves | 34 |
| Caracteres | Sólo ASCII, sin comillas simples |
| SHA-256 | `f94db8d2aafd90c09374eac0f3e2a5a6adc64dda97fc2465529698a6e7b425d1` |

Cambiar la configuración genera otro identificador de preset; `grid-consulta-v1` no se altera. Las pruebas publicadas comparan contra la constante y su huella, sin depender de `KnowledgeSource`.

## 2. Los diez registros

Responsable de cada valor: **U** usuario (formulario), **P** plantilla, **G** constante del generador según Designer, **A** ambiente (resuelto en la base destino), **I** identidad autenticada de la request.

Auditoría estándar (`std`): `User_Create` = I, `User_Update` = `NULL`, `F_Create` = `GETDATE()`, `F_Update` = `NULL`. Designer escribe `NULL` en `User_Update` al crear; el `''` que aparece en los exports es representación del exportador.

Fuente: `KnowledgeSource/DesignerRealEvidence/Clases/EngineUpdater.cs` (línea indicada) y la grilla inicial de `vpruebaJFAL.sql`.

| # | Tabla | ID y relaciones | Columnas de datos | Auditoría | Fuente |
|---|---|---|---|---|---|
| 1 | Vistas | PK `IdVista` = nombre (U); `IdOwner` = 1 (G) | `Nombre` = `IdVista` (U); `Descripcion` (U, por defecto el nombre); `FechaCreacion` = `GETDATE()`; `VersionActual` y `VistaVersionado` = `'1.0'` (G); `TipoVista` = `'go'` (P); `UsaSeguridad` = 0 (G); `IdHistoricoCambio` = 1 (G); `ArchivoJs`, `JsCode`, `Frente` = `''`; `IdStringsConnectionMyVision` = `NULL` | std | L853 |
| 2 | Estados | `IdEstado` = `@IDX_Estado`; `IdVista` = nombre | `[Default]` = 1; `Descripcion` = `'Estado01'` (P); `ArchivoJs` = `''`; `LeftMenuFijo` = 0 | std | L1526 |
| 3 | Filas | `IdFila` = `@IDX_Fila`; `IdEstado` = `@IDX_Estado` | `Nombre` = `'Fila01'` (P); `Ancho` = 0; `Altura` = 0 | std | L1428 |
| 4 | Areas | `IdArea` = `@IDX_Area`; `IdFila` = `@IDX_Fila` | `Nombre` = `'Area01'` (P); `Ancho` = 0; `Altura` = 0 | std | L886 |
| 5 | ExtraccionesDatos | `IdExtraccionDato` = `@IDX_ExtraccionDato` | `Descripcion` = SP canónico (U, canonizado) | std | L129 |
| 6 | StoredProcedure | `IdStoredProcedure` = `@IDX_StoredProcedure`; `IdExtraccionDato` = `@IDX_ExtraccionDato`; `IdStringConnection` = `@IdStringConnection` (A) | `Nombre` = SP canónico (columna `varchar`, literal sin `N`); `Descripcion` = `''`; `OrdenEjecucion` = 0; `IdTipoEjecucionSP` = 1 (G, «Siempre») | std | L144 |
| 7 | ObjetosDeArea | `IdObjetoDeArea` = `@IDX_ObjetoArea`; `IdArea` = `@IDX_Area`; `IdExtraccionDato` = `@IDX_ExtraccionDato` | `IdTipoObjetoDeArea` = 2 (P, «Grilla»); `Nombre` = `'Grilla01'` (P); `UsaPaginador` = 0 | std | L927 |
| 8 | ConfiguracionesObjetoDeArea | `Id` = `@IDX_ObjetoAreaConfig`; `IdObjetoDeArea` = `@IDX_ObjetoArea` | `Configuracion` = preset `grid-consulta-v1` literal (P) | std | L1071; export L112 |
| 9 | LeftMenus | `IdLeftMenu` = `@IDX_LeftMenu`; `IdEstado` = `@IDX_Estado` | `Nombre` = `'LMFiltros'` (P) | std | L1361 |
| 10 | ConfiguracionesEstados | PK `IdEstado` = `@IDX_Estado` | `Configuracion` = `{"filters":"","leftMenus":"","isPopUp":false,"popUp":{},"idLayout":"00"}` (P) | Sólo `User_Create` = I y `F_Update` = `GETDATE()`; la tabla no tiene más columnas de auditoría | L1592 |

Notas:

- El orden de inserción es el de la tabla y coincide con el de Designer.
- Sólo existen claves foráneas reales en Vistas→Owners, StoredProcedure→StringsConnection y TiposEjecucionesSP, LeftMenus→Estados y ConfiguracionesEstados→Estados. El resto de relaciones depende únicamente de las variables.
- La plantilla no escribe `ParametrosSP`, `MapeoParametros`, `WebControls`, `Handlers` ni `VistaVersionado`. Para una vista nueva Designer tampoco inserta `VistaVersionado` (L2002).
- `IdStringConnection` no se incrusta como número: el script lo resuelve por alias dentro de la transacción, en la base donde se aplica.

## 3. Nombres, longitudes y Stored Procedure

### Nombres

`Estado01`, `Fila01`, `Area01` y `Grilla01`, según el acuerdo de fase 1. Designer escribe por convención `<estado>F<n>` para filas y `<área>_Obj` para objetos; los nombres acordados son válidos por esquema y se mantienen. **La compatibilidad al abrir y guardar la vista en Designer no fue inspeccionada.**

### Longitudes efectivas

| Dato | Límite | Motivo |
|---|---|---|
| Nombre / IdVista | 250 | `Vistas.Nombre`, `Vistas.IdVista`, `Estados.IdVista` |
| Descripción | 350 | `Vistas.Descripcion` |
| Estado, fila, objeto | 250 | Columnas de nombre correspondientes |
| Área | 75 | `@NombreArea nvarchar(75)` en los procedimientos de recuperación, aunque `Areas.Nombre` admite 250 |
| Stored Procedure | 250 | `ExtraccionesDatos.Descripcion`, aunque `StoredProcedure.Nombre` admite 750 |

### Nombre de vista

Recortado, no vacío, dentro de la longitud y escrito como literal `N`. Sus columnas son `nvarchar`, así que no hay pérdida de información.

Restringir sus caracteres sería una regla funcional nueva y es una decisión separada del usuario (sección 9). Mientras no se decida, no se aplica ninguna restricción adicional.

### Stored Procedure

- **Entrada admitida:** `esquema.nombre` o `[esquema].[nombre]`.
- **Caracteres por parte:** letras ASCII, dígitos y guion bajo, sin empezar por dígito; máximo 128 por parte.
- **Rechazos, sin transformación silenciosa:** nombre sin esquema (su resolución depende del esquema por defecto de quien ejecuta), nombres de tres o más partes, y nombres con espacios, acentos u otros símbolos.
- **Forma almacenada:** `[esquema].[nombre]`, construida con los nombres tal como figuran en el catálogo de la conexión del SP y vueltos a validar contra el mismo conjunto de caracteres. Sólo procedimientos SQL (tipo `P`).
- **Sin pérdida:** al ser sólo ASCII, el valor es idéntico en `varchar` bajo cualquier página de códigos y en `nvarchar`. Se guarda igual en `StoredProcedure.Nombre` y en `ExtraccionesDatos.Descripcion`.
- **En el ViewSpec** el SP sigue siendo lógico (`dbo.PRC_GET_GRID_TEST_SORT_COLUMN`); la forma con corchetes pertenece al plan físico.

Los tres exports reales de Designer disponibles guardan la forma con corchetes en 26 de 27 casos; el restante no lleva esquema.

### Parámetros

La plantilla admite un SP sin parámetros, o la excepción del SP acordado con su único parámetro `@QueryString`. No se escriben `ParametrosSP` ni `MapeoParametros`.

`sys.parameters.has_default_value` vale 0 para ese SP aunque declara `= ''`: la metadata no permite saber de forma fiable si un parámetro T-SQL es opcional. Ampliar la regla a «cualquier SP cuyo único parámetro sea `@QueryString`» **no está aprobado** y se reconsidera después de comprobar el comportamiento en Motor.

## 4. Validación y preflight

### Camino único

```text
normalizar → validar estructura → validar forma generable → preflight → plan → script
```

Cualquier hueco en cualquier paso devuelve el resultado sin script. No existe un atajo que construya el plan sin pasar por la validación de generación.

Formas generables:

1. La vista mínima vacía actual (sección 7).
2. Exactamente un estado inicial, una fila y una grilla, con todos los nombres, SP, alias y preset conocido, y sin handlers.

Los nombres de estado se comparan con una sola regla en el validador y en la construcción del plan; los estados duplicados son un error.

### Preflight del primer recorrido

Entrada: SP `dbo.PRC_GET_GRID_TEST_SORT_COLUMN`, alias `GestionEngine`, mapeado a la conexión `GestionEngineLocal`. En este recorrido la conexión del alias es la misma que la de metadata.

| # | Comprobación | Cómo | Resultado observado con el SP de prueba | Rechazo si falla |
|---|---|---|---|---|
| 0 | Forma generable, nombres y forma del SP | C#, sin base de datos | Pasa por diseño | Lista de huecos |
| 1 | Nombre disponible | `Vistas` por `IdVista` o `Nombre` | Depende del nombre elegido | «Nombre de vista existente» |
| 2 | Alias en el catálogo | `StringsConnection`, hasta dos filas | Una fila, no Oracle | «Alias inexistente», «Alias ambiguo» o «Alias no es SQL Server» |
| 3 | Conexión configurada para el alias | Mapeo en configuración, sin secretos | La clave se crea en fase 3 | «Alias sin conexión configurada» |
| 4 | El SP existe | `sys.procedures` con `sys.schemas`, tipo `P` | `[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]` | «SP inexistente o no visible para DesignerIA» |
| 5 | Parámetros | `sys.parameters` | Sólo `@QueryString`, de entrada: excepción acordada | «SP con parámetros no admitidos por la plantilla» |
| 6 | Primer resultado | `sys.dm_exec_describe_first_result_set_for_object`, sólo con el objeto ya resuelto | 6 columnas sin error; 0 = `codigo`, 1 = `Agrupador` | «Resultado incompatible» o «Resultado no determinable» |

- Los pasos 1 y 2 se evalúan siempre; 4 a 6 sólo si 2 y 3 pasan.
- El paso 6 es análisis estático de metadata: no ejecuta el SP. No se usa `SET FMTONLY ON` ni lectura de esquema por ejecución.
- El preset fija la columna 0 como llave del renglón y la 1 como su descripción. «Compatible» significa que el primer resultado es determinable y tiene al menos dos columnas.
- Con el objeto sin resolver, la función del paso 6 puede devolver columnas igualmente: el preflight exige existencia antes.
- El script vuelve a comprobar nombre y alias en la base donde se aplica; no revalida el SP.

Los resultados de la tabla se observaron por consultas de metadata de sólo lectura en el ambiente local definido en Contexto. No son una prueba del preflight, que no está implementado.

### Límites para otros SP y ambientes

Son límites documentados de esta entrega. No bloquean el primer recorrido y se revisan cuando un SP real lo necesite.

- Otros alias necesitan un mapeo explícito a una conexión; no se descubren servidores ni bases.
- Los alias Oracle no se admiten.
- En servidores anteriores a SQL Server 2012 la función del paso 6 no existe: resultado no determinable.
- Un SP que usa tablas temporales o SQL dinámico puede dar resultado no determinable.
- Un SP que la identidad de la API no puede ver es indistinguible de uno inexistente.
- El preflight vale para el ambiente y el momento en que se ejecutó. Si el SP se elimina después, la vista se crea igual y el fallo aparece en Motor.
- «Compatible» no acredita que la grilla se muestre bien: eso lo decide la comprobación en Motor.

Ante un resultado incompatible o no determinable no se genera script; no existe la opción de generar de todos modos.

## 5. Script generado

El script es texto determinístico generado en C#. Debe ser compatible con SQL Server 2008 R2. Generarlo no autoriza ejecutarlo.

```sql
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'El script requiere una sesión sin transacción abierta.', 16, 1);
    RETURN;
END

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.Vistas WITH (UPDLOCK, HOLDLOCK)
               WHERE IdVista = N'<vista>' OR Nombre = N'<vista>')
        RAISERROR(N'La vista ya existe; el script no realiza cambios.', 16, 1);

    DECLARE @IdStringConnection int, @AliasCount int;
    SELECT @AliasCount = COUNT(*), @IdStringConnection = MIN(IdStringConnection)
    FROM dbo.StringsConnection WHERE StringConnection = N'<alias>';
    IF @AliasCount <> 1
        RAISERROR(N'El alias de conexión no existe o es ambiguo en esta base.', 16, 2);

    DECLARE @IDX_Estado int, @IDX_Fila int, @IDX_Area int, @IDX_ExtraccionDato int,
            @IDX_StoredProcedure int, @IDX_ObjetoArea int, @IDX_ObjetoAreaConfig int, @IDX_LeftMenu int;
    SELECT @IDX_Estado = ISNULL(MAX(IdEstado), 1) + 1 FROM dbo.Estados WITH (UPDLOCK, HOLDLOCK);
    SELECT @IDX_Fila = ISNULL(MAX(IdFila), 1) + 1 FROM dbo.Filas WITH (UPDLOCK, HOLDLOCK);
    SELECT @IDX_Area = ISNULL(MAX(IdArea), 1) + 1 FROM dbo.Areas WITH (UPDLOCK, HOLDLOCK);
    SELECT @IDX_ExtraccionDato = ISNULL(MAX(IdExtraccionDato), 1) + 1 FROM dbo.ExtraccionesDatos WITH (UPDLOCK, HOLDLOCK);
    SELECT @IDX_StoredProcedure = ISNULL(MAX(IdStoredProcedure), 1) + 1 FROM dbo.StoredProcedure WITH (UPDLOCK, HOLDLOCK);
    SELECT @IDX_ObjetoArea = ISNULL(MAX(IdObjetoDeArea), 1) + 1 FROM dbo.ObjetosDeArea WITH (UPDLOCK, HOLDLOCK);
    SELECT @IDX_ObjetoAreaConfig = ISNULL(MAX(Id), 1) + 1 FROM dbo.ConfiguracionesObjetoDeArea WITH (UPDLOCK, HOLDLOCK);
    SELECT @IDX_LeftMenu = ISNULL(MAX(IdLeftMenu), 1) + 1 FROM dbo.LeftMenus WITH (UPDLOCK, HOLDLOCK);

    -- Diez INSERT en el orden de la sección 2.

    IF (SELECT COUNT(*) FROM dbo.Filas WHERE IdFila = @IDX_Fila) <> 1
        RAISERROR(N'IdFila duplicado; no se realizan cambios.', 16, 3);
    IF (SELECT COUNT(*) FROM dbo.Vistas WHERE IdVista = N'<vista>' OR Nombre = N'<vista>') <> 1
        RAISERROR(N'Nombre o IdVista duplicado; no se realizan cambios.', 16, 4);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @ErrorNumber int, @ErrorState int, @ErrorMessage nvarchar(2048);
    SELECT @ErrorNumber = ERROR_NUMBER(), @ErrorState = ERROR_STATE(), @ErrorMessage = ERROR_MESSAGE();
    RAISERROR(N'[%d/%d] %s', 16, 1, @ErrorNumber, @ErrorState, @ErrorMessage);
END CATCH

SET LOCK_TIMEOUT -1;
SET XACT_ABORT OFF;
```

### Literales

- `N'…'` en todas las columnas `nvarchar` y `ntext`.
- Literal simple en `StoredProcedure.Nombre`, que es `varchar`; su contenido es sólo ASCII por la regla de la sección 3.
- Todo texto externo pasa por el mecanismo central de escape (`SqlLiteralEscaper`).

### Colisión por `Nombre` e `IdVista`

- `IdVista` es la clave primaria; `Nombre` no tiene índice ni restricción.
- La lectura con `UPDLOCK, HOLDLOCK` recorre toda la tabla por el filtro sobre `Nombre`. La intención es que, hasta el `COMMIT`, ninguna otra sesión pueda insertar ni modificar filas de `Vistas`, sea por `IdVista` o por `Nombre`.
- La igualdad la decide la base con su collation; C# no decide igualdad de nombres.
- La guarda final exige exactamente una fila con ese `IdVista` o `Nombre` antes de confirmar.
- No se combina con `TABLOCK`: con un bloqueo de tabla, `UPDLOCK` pasa a exclusivo y bloquearía a los lectores.

### Asignación de IDs

- **Requisito:** `IdFila` debe ser único entre vistas, aunque `PK_Filas` sea `(IdFila, IdEstado)` y no lo imponga. `recuperarDatosObjetoDeArea` une áreas con filas sólo por `IdFila`, y todas las vistas de la plantilla usan `Area01`.
- Las ocho tablas con ID generado tienen clave primaria agrupada encabezada por esa columna. La intención del `UPDLOCK, HOLDLOCK` sobre `MAX` es retener el rango final hasta el `COMMIT`.
- El orden de bloqueo es fijo: `Vistas` primero y después los ocho IDs en el orden de inserción de Designer.
- La guarda de `IdFila` hace cumplir el requisito aunque el bloqueo no se comporte como se supone.
- Sobre tabla vacía el primer ID es 2, igual que en Designer y en el generador actual.

### Errores

Todo error que pasa por el `CATCH` llega al cliente como **50000, estado 1**. El número y el estado originales sólo viajan dentro del texto, con el prefijo `[número/estado]`. El cliente no recibe los números originales y no puede distinguir los casos sin interpretar ese prefijo; cómo hacerlo se define en fase 5.

| Situación | Lo que recibe el cliente | Texto | Estado final |
|---|---|---|---|
| Nombre o `IdVista` existente | 50000, estado 1 | `[50000/1] La vista ya existe…` | Sin cambios |
| Alias inexistente o ambiguo en destino | 50000, estado 1 | `[50000/2] El alias de conexión…` | Sin cambios |
| `IdFila` duplicado | 50000, estado 1 | `[50000/3] IdFila duplicado…` | Sin cambios |
| Nombre o `IdVista` duplicado al final | 50000, estado 1 | `[50000/4] Nombre o IdVista duplicado…` | Sin cambios |
| Espera de bloqueo agotada | 50000, estado 1 | `[1222/n] …` | Sin cambios |
| Víctima de interbloqueo | 50000, estado 1 | `[1205/n] …` | Sin cambios |
| Clave duplicada | 50000, estado 1 | `[2627/n] …` | Sin cambios |
| Transacción previa abierta | 50000, estado 1, sin pasar por el `CATCH` | Sin prefijo | No se inicia nada |
| Cancelación o tiempo agotado del cliente | El error propio del cliente | — | `XACT_ABORT` revierte; las opciones quedan activas en esa sesión |

### Tiempos de espera y opciones de sesión

- `LOCK_TIMEOUT = 5000` limita **cada espera de bloqueo por separado**. No limita la duración total de la transacción, que puede encadenar varias esperas, ni el tiempo máximo que Motor u otra sesión pueden quedar esperando los bloqueos del script.
- Mientras espera, el script retiene los bloqueos ya tomados, incluido el de `Vistas`.
- Si el motor de base de datos escala a bloqueo de tabla, también esperan los lectores de `Vistas`. No se promete ausencia de bloqueos de lectura.
- Una lectura que recorra filas nuevas sin confirmar puede esperar, según el aislamiento de la base.
- Un guardado simultáneo de Designer puede producir interbloqueo: `eliminarVista` borra primero las tablas hijas y `Vistas` al final, y después inserta en el orden inverso. Ningún orden de bloqueos lo elimina; el resultado esperado es que una de las dos transacciones se revierta completa.
- `SET LOCK_TIMEOUT -1` y `SET XACT_ABORT OFF` restablecen los valores por defecto de SQL Server. **No restauran las opciones que tuviera la sesión antes.** `SET LOCK_TIMEOUT` no acepta variables, así que un valor previo distinto no puede reponerse.
- `XACT_ABORT ON` evita que una cancelación o un tiempo agotado del cliente deje la transacción abierta.

**Esta protección es diseño por lectura de esquema y documentación. No está comprobada**: no se probó concurrencia, reversión, cancelación ni comportamiento en SQL Server 2008 R2.

## 6. Aplicación en fase 4

La aplicación de prueba requiere autorización explícita de escritura y sigue este procedimiento:

1. **Sesión dedicada:** una conexión nueva, usada sólo para este script.
2. **Sin transacción previa:** comprobar `SELECT @@TRANCOUNT` = 0 antes de ejecutar. El script además lo exige, porque su `ROLLBACK` revertiría también una transacción ajena.
3. **Antes de ejecutar:** registrar los máximos de las ocho columnas de ID y que el `IdVista` no existe.
4. **Ejecución:** un solo batch, sin `GO` intermedios.
5. **Ante cancelación o tiempo agotado del cliente:** en esa misma sesión comprobar `@@TRANCOUNT`; si es mayor que 0, ejecutar `ROLLBACK`. Después cerrar la sesión y no reutilizarla, porque conserva las opciones cambiadas.
6. **Verificación desde otra sesión:** tras un fallo, cero filas del `IdVista` en las diez tablas y máximos sin cambio; tras un éxito, los diez registros y sus relaciones.

«Metadata local comprobada» y «Motor comprobado» siguen siendo checkpoints distintos.

## 7. Vista mínima vacía

El generador actual produce una vista con un estado vacío. Su comportamiento se conserva:

- Cuatro registros: Vistas, Estados, Filas y ConfiguracionesEstados.
- `TipoVista` = `site`; estado `Pantalla`; fila `PantallaF0`; misma configuración de estado, mismos valores y auditoría.
- Mismo primer ID sobre tabla vacía.

Cambios intencionales en el texto de su script:

1. Prefijo `N` en los literales de columnas `nvarchar`.
2. Guarda inicial de `@@TRANCOUNT`.
3. Comprobación de colisión dentro de la transacción, con bloqueo.
4. Lectura de `MAX` con `UPDLOCK, HOLDLOCK`.
5. `SET XACT_ABORT ON` y `SET LOCK_TIMEOUT`, con su restablecimiento final.
6. Guardas finales de `IdFila` y de nombre.
7. Prefijo `[número/estado]` en el mensaje de error.

Diferencia preexistente que no se modifica: la vista vacía no inserta `LMFiltros`, aunque Designer lo crea para el estado default (L1358).

Hoy no existe ninguna prueba automática del generador. La regresión se escribe antes de tocar el código (ver plan de fase 3).

## 8. Defectos del código actual que el plan corrige

Observados por lectura; no se ejecutaron.

- `ViewScriptController` construye el plan sin pasar por `ViewScriptGenerationValidator` cuando el ViewSpec no tiene errores. Un spec con áreas o con pendientes produce hoy sólo los cuatro registros de la vista vacía, sin avisar.
- `ViewCreationPlanFactory` busca el estado inicial con comparación exacta y `Single`; el validador ignora mayúsculas y no detecta estados duplicados.
- `ViewSpecNormalizer` reconstruye el ViewSpec con cuatro argumentos: un `ViewType` nuevo se perdería.
- `StringConnectionCatalogService` devuelve la primera fila y no detecta alias duplicados. No tiene llamadores hoy.
- El generador fija `site`, escribe literales sin `N` y comprueba la colisión fuera de la transacción.

## 9. Decisiones

| Decisión | Estado |
|---|---|
| `UsaSeguridad = 0` para las vistas nuevas de esta entrega | **Resuelta**: confirmada por el usuario. Coincide con el default de Designer. Su efecto en Motor no fue inspeccionado |
| Nombres `Estado01`, `Fila01`, `Area01`, `Grilla01` | Resuelta en fase 1; no se reabre |
| Ampliación de ViewSpec, preset inmutable y camino único de validación | Revisada |
| Forma almacenada del SP con corchetes y caracteres admitidos | Revisada |
| Parámetros: sin parámetros, más la excepción del SP acordado | Revisada; la extensión general no está aprobada |
| Rechazo cuando el resultado del SP no puede determinarse | Revisada como límite de esta entrega |
| Protección transaccional, tiempos de espera y formato de error | Revisada como diseño; pendiente de prueba |
| Restricción de caracteres del nombre de vista | **Abierta**, opcional, del usuario. Sin decisión no se aplica |

## 10. Comprobaciones pendientes

Ninguna está hecha.

- Generación real del script y pruebas automáticas (fase 3).
- Aplicación autorizada y coherencia de los diez registros (fase 4).
- Concurrencia: dos creaciones con nombres distintos; dos con el mismo nombre; creación simultánea con un guardado estilo Designer sobre la vista con los IDs más altos.
- Colisión por `Nombre` con `IdVista` distinto.
- Reversión forzada antes del `COMMIT`: cero filas, máximos sin cambio y sin transacción abierta.
- Espera de bloqueo agotada y cancelación a mitad del script, con el cierre de sesión de la sección 6.
- Lectura de `Vistas` y de otra vista durante la espera, registrando si bloquea y cuánto.
- Compatibilidad del script con SQL Server 2008 R2.
- Render en Motor: grilla visible con los datos esperados, efecto de `UnidadAltoFila` = `817`, comportamiento de `@QueryString` sin mapeo y de `UsaSeguridad = 0`.
- Compatibilidad al abrir y guardar la vista en Designer.

## 11. Evidencia y límites

- **Consultado:** el código del flujo actual, `vpruebaJFAL.sql` y los otros dos exports, `EngineUpdater.cs`, `Designer.js`, `Imports/ScriptCreateGestionEngine.sql`, la Wiki para las columnas de código y descripción, y consultas de metadata de sólo lectura en el ambiente local definido en Contexto.
- **Motor:** su código no está en el repositorio. Lo dicho sobre él procede de lecturas previas del revisor y no fue reverificado aquí.
- **Designer:** el backend que descubre los parámetros de un SP no está en `KnowledgeSource`.
- **Base local:** SQL Server 2022 con datos de laboratorio. No acredita compatibilidad con 2008 R2 ni convenciones de producción.
- **Sin ejecutar:** ningún SP, script, build ni prueba durante esta fase.

`KnowledgeSource` es evidencia de dominio fuera de Git; no es runtime y las pruebas publicadas no deben depender de esa carpeta.

## Criterio de cierre de fase 2

Campos, defaults, nombres, componentes y relaciones definidos con evidencia, y plan de fase 3 revisado. No equivale a implementación ni a prueba en Motor.
