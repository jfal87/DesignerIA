# DesignerIA — Checkpoint: creación desde JSONMetadata

Fecha: 13 de octubre de 2026, flujo completo de creación local. Contexto breve para retomar sin el historial del chat.

## Flujo completo — 13 de octubre de 2026

- A petición del usuario: `prepare` devuelve también el script SQL generado con su identidad autenticada. El diálogo lo muestra y permite copiarlo. Al crear, el backend reconstruye y valida el plan y el SQL: nunca acepta el SQL de la pantalla para ejecutarlo. El resultado conserva el script y refresca el conteo de vistas.
- Creación habilitada en `appsettings.Development.json` para el mismo usuario Windows autorizado; conexión efectiva local `LAPTOP-GM0SHH26 / GestionEngine`. No se cambiaron autenticación, dependencias, procesos de Visual Studio ni Git.
- El usuario confirmó que el enlace es mock. Se reutiliza la base de referencia `https://desagestion.ternium.net`, con `Motor:IsMock=true`; la API devuelve `ViewUrlIsMock` y la pantalla lo etiqueta como enlace de referencia. No acredita acceso a la vista local ni una prueba de Motor.
- Prueba real: `vDemoEstados01` creada por la API; `Created`, **24 registros verificados**. SELECT independientes confirman `Principal` inicial, `Detalle` no inicial, una grilla por estado, ambos controles alojados en Principal y activación diferenciada por estado. Total de vistas: 22. No se ejecutó ningún SP de negocio.
- Solución completa compila sin errores; **959 pruebas aprobadas, 0 fallidas**. Evidencia en `artifacts/flujo-json-20261013/`: script `vDemoEstados01.sql`, preparación, creación, lectura independiente y TRX. Recuperación previa en `antes/`.
- Visual Studio no se reinició. Para ver los contratos y la UI nuevos se requiere reiniciar API y Web desde Visual Studio y recargar el navegador. El flujo se probó por API propia, no pulsando en el navegador. Designer y Motor reales permanecen pendientes.

## Dirección

Crear vistas desde un documento **JSONMetadata** compacto: el usuario escribe lo necesario, DesignerIA completa las convenciones comprobadas, señala lo que falta y crea metadata que pueda abrirse y afinarse en Designer. La dirección posterior son estructuras grandes con varios estados, áreas, controles y left menu, sobre el mismo contrato. Se preservan el chat y el RAG, el conocimiento curado, «Enseñar a DesignerIA», la preparación por lenguaje natural y `ViewSpec`.

## Estado

```text
Análisis y limpieza:      ENTREGADOS
Contrato 1.0:             DEFINIDO; ampliado con web controls de navegación y varios estados
Primera entrega (T1):     IMPLEMENTADA
Prueba real:              vDemoGrilla01 CREADA Y VERIFICADA EN LA METADATA LOCAL
Segunda entrega (T1b):    vDemoEstados01 CREADA Y VERIFICADA EN LA BASE LOCAL
Designer y Motor:         SIN COMPROBAR: no están desplegados en este equipo
Repositorio:              C:\Code\DesignerAI · rama feature/create-view-executor · HEAD 28532c2
Árbol de trabajo:         todo sin confirmar: fase 6.1, limpieza, las dos entregas y documentos
Git:                      sin escrituras
SQL:                      altas autorizadas de vDemoGrilla01 y vDemoEstados01; el resto, lecturas acotadas
```

## Documentos

| Documento | Contenido |
|---|---|
| `DesignerIA-JSONMetadata-Entrega2.md` | **Empezar aquí.** Prueba real, límite del documento, segunda entrega, cambios, cobertura exacta y reservas |
| `DesignerIA-JSONMetadata-Contrato.md` | Contrato, omisiones, recorrido, transición, responsabilidades, comprobaciones pendientes y decisiones |
| `capacidades/` y `ejemplos/` | Fichas por capacidad y cuatro documentos de ejemplo |
| `DesignerIA-JSONMetadata-Entrega1.md` | Primera entrega (histórico) |
| `DesignerIA-JSONMetadata-Analisis.md` | Evidencia de Designer y Motor, y huecos |
| `DesignerIA-JSONMetadata-Limpieza.md` | Limpieza aplicada |

## Qué existe ahora

- Botón «Crear vista» → diálogo de archivo → `api/view-json/prepare` → revisión de la definición efectiva, el origen de cada dato y los diagnósticos → confirmación → `api/view-json/create`.
- Se materializa: vista vacía `site`; vista `go` con uno o varios estados, cada uno con una fila, un área y una grilla con parámetros de valor fijo; web controls `buttonList` con opciones estáticas y `cambiarEstado`, definidos en la vista y activados por estado.
- Convenciones: descripción = identificador; único estado `Pantalla` e inicial; única área `Area01`; fila `<estado>F0`; objeto `<área>_Obj`; configuración de grilla de referencia; controles escritos una vez en el estado inicial; `LMFiltros` sólo en el estado inicial.
- El documento recibido y su definición efectiva comparten el mismo límite de 256 KB.
- Todo lo demás del contrato (left menu, otros controles y acciones, fuentes por selección, varias áreas por fila, `settings`, handlers, gráfico, JS, pop-up, pivot…) se rechaza con su ubicación.
- Las dos entradas (documento y `ViewSpec`) producen el mismo plan y comparten generación, ejecución y verificación. Las rutas por `ViewSpec` y el chat no cambiaron.

## Comprobado

- Compilación con 0 errores; 952 pruebas (857 heredadas sin modificar, 46 del documento y 49 de varios estados y controles).
- `vDemoGrilla01` creada por el flujo JSON en `LAPTOP-GM0SHH26` / `GestionEngine`; la API verificó 10 registros, relaciones y valores; lecturas independientes lo confirman. Evidencia: `artifacts/prueba-real-vDemoGrilla01/`.
- En vivo y sin escritura: los cuatro ejemplos, la reentrada de la definición efectiva y el rechazo de la creación deshabilitada.

## Cierre de revisión con Codi — 11 de octubre de 2026

- Claudio completó las capacidades de la segunda entrega. La revisión detectó y corrigió un hueco de verificación: con varios estados, los parámetros y mapeos ahora deben pertenecer al estado y área de su propia grilla; pertenecer a cualquier SP de la vista ya no basta. La lectura informa esas relaciones lógicas sin inventar identificadores físicos.
- Seis regresiones puras fallaron antes de la corrección y pasan después, incluidos propietarios equivocados o ausentes y el mismo nombre de parámetro en ambos estados. Las 857 pruebas heredadas permanecen sin cambios por esta revisión.
- Compilación de API, Contracts y pruebas: 0 errores. Suite actual: **958 aprobadas, 0 fallidas, 0 omitidas** (857 heredadas, 46 del documento y 55 de estados/controles). Evidencia: `artifacts/codi-relaciones-20261011/pruebas/suite-final-corpus.trx`; recuperación previa y regresión inicial en la misma carpeta.
- Para las pruebas heredadas del corpus se usa `--artifacts-path artifacts`: sus fixtures resuelven el repositorio subiendo cuatro niveles desde el binario. La primera salida anidada causó 199 fallos de ruta del corpus; al usar la profundidad prevista la suite completa pasó, sin cambiar RAG ni sus fixtures.
- Validación de esta corrección: compilación y pruebas sin SQL. La consulta de lectura modificada no se ejecutó contra la base. No se crearon vistas, no se tocaron los procesos de Visual Studio ni la configuración de creación, y no se escribió en Git. Permanecen las reservas de Designer/Motor y de ejecución real de varios estados.

## Pendiente

1. **Designer y Motor** para `vDemoGrilla01`: abrirla, guardarla desde Designer y comparar con el «antes» conservado (`05-metadata.sql` y su salida); verla con datos en Motor; abrirla por enlace con `Motor:BaseUrl` configurado. Falta un Designer y un Motor desplegados contra la base local.
2. **Designer y Motor para varios estados:** `vDemoEstados01` ya está creada; falta comprobar visualmente la navegación y el guardado. No recrearla: una carga del ejemplo ahora informa nombre existente.
3. `LMFiltros` en la vista vacía, según lo que muestre Designer.
4. Siguientes incrementos, sin iniciar: left menu con contenido, fuentes de parámetro por selección, varias áreas por fila, gráficos, JS; pop-up y Pivot; Word/TXT; migración del lenguaje natural (T2).
5. Heredado: export de `sic_budget_costo_variable`; conocimiento curado como fuente de una respuesta sin validar en vivo.

## Fronteras vigentes

Este documento no autoriza nada. Git de escritura, SQL, dependencias, autenticación, publicación y despliegue dependen de la instrucción explícita del usuario y de `AGENTS.md`. Se autorizaron y ejecutaron las altas de `vDemoGrilla01` y `vDemoEstados01`; no sobrescribirlas, borrarlas ni recrearlas. El usuario pidió habilitar el flujo local: creación encendida en Development para su identidad Windows. Otras escrituras realizadas por agentes requieren autorización explícita.

## Al retomar

Recontrastar rama, `HEAD` y `git status --short`. Recuperaciones fuera del código productivo: `artifacts/limpieza-20261011/`, `artifacts/entrega-json-20261011/` y `artifacts/entrega2-estados-20261011/` (antes de la segunda entrega). Antes de editar código, crear una nueva. No descartar cambios. No detener los procesos de Visual Studio: compilar con `--artifacts-path` y usar una instancia propia en el puerto 5299.
