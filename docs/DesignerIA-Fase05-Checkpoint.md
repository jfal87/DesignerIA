# DesignerIA — Checkpoint de cierre funcional de las fases 4 y 5: V1 entregada con reserva

Fecha: 11 de octubre de 2026. Contexto breve para los chats nuevos de Claudio y del revisor. No requiere transportar conversaciones anteriores. Sustituye a la versión anterior de este archivo, que llegaba hasta la «creación local comprobada» y era previa a la entrega de URL.

> **Nota posterior del mismo día.** Este checkpoint queda como **registro histórico** del cierre de V1. Después de escribirlo se retiraron el formulario y la galería «Nueva vista», la plantilla «Consulta con grilla» (`GridQueryTemplate`, `ViewTemplateController`, `GridQueryTemplateRequest`) y la vista previa simulada (`ViewSpecPreview.razor`). Siguen existiendo la generación, la creación, la verificación y la devolución de URL, alimentadas por el ViewSpec que prepara el chat. La sección «Vista previa simulada» y el paso 2 de la reserva (preparar desde la Web una «Consulta con grilla») ya no describen el producto. Estado vigente: `DesignerIA-JSONMetadata-Checkpoint.md`.

## Decisión de cierre del 11 de octubre de 2026

**El usuario aceptó el cierre funcional de las fases 4 y 5 para el alcance probado y registró V1 como entregada con una reserva de validación integrada.** Es una decisión explícita del usuario, no una conclusión de las pruebas.

Alcance probado sobre el que se acepta el cierre:

1. Creación automática en el ambiente local, desde la API y desde la Web, con verificación independiente.
2. Script generado por DesignerIA aplicado manualmente en desarrollo.
3. Esa vista, `vDesignerIATest01`, abierta en Designer y con su grilla mostrando datos en Motor.

**Reserva de validación integrada.** Queda pendiente una comprobación: crear automáticamente con Motor configurado, recibir IdVista y URL reales y abrir la vista mediante «Abrir vista». **No se ha ejecutado** y no bloquea el cierre aceptado. Ver «Reserva: comprobación integrada pendiente».

Los criterios originales de `DesignerIA-Roadmap.md` se conservan como referencia. La aceptación no afirma que el criterio de la fase 5 se haya recorrido completo: la creación automática se comprobó en un ambiente sin Motor y la vista que Motor mostró se aplicó a mano. Lo que une ambas mitades es la reserva.

## Estado

- **Fases 4 y 5:** cierre funcional aceptado por el usuario para el alcance probado.
- **V1:** entregada, con la reserva de validación integrada abierta.
- **Implementación:** DesignerIA prepara la plantilla «Consulta con grilla», genera el script, crea la vista desde la API y desde la Web, verifica el resultado y devuelve IdVista y URL de Motor cuando el ambiente tiene una base de Motor configurada.
- **La creación está apagada** en `appsettings.json` y en `appsettings.Development.json`. Sólo se habilitó por variable de entorno en procesos propios de prueba, siempre contra la base local.
- **Aplazado:** el arranque en la VM y la autenticación del proxy. No se documentan ni se resuelven aquí.

## Evidencia por origen

Tres orígenes distintos, que no deben confundirse:

- **Evidencia manual del usuario:** capturas y URL que el usuario compartió en el chat. No está archivada en el repositorio ni en `artifacts/`, y Claudio no ejecutó ni presenció esa prueba.
- **Evidencia archivada:** informes, respuestas, logs y capturas guardados en `artifacts/` del equipo de desarrollo.
- **Pruebas realizadas por Claudio:** las ejecutadas dentro de los encargos autorizados; su evidencia es la archivada.

| Evidencia | Origen | Qué acredita | Qué no acredita |
|---|---|---|---|
| Script generado por DesignerIA, aplicado a mano en desarrollo | Manual del usuario; compartida en el chat, no archivada | El script del generador se aplica en la base de metadata de desarrollo | La creación automática desde la API |
| `vDesignerIATest01` abierta en Designer | Manual del usuario; compartida en el chat, no archivada | Designer lee la vista generada | Que Designer la guarde sin alterarla |
| Grilla de `vDesignerIATest01` mostrando datos en Motor | Manual del usuario; capturas y URL compartidas en el chat, no archivadas | Motor interpreta la metadata de la plantilla y ejecuta su extracción | La URL que construye DesignerIA ni el enlace «Abrir vista» |
| Fase 4, casos 01–06 | Prueba de Claudio; archivada | Comportamiento del script en la base local: creación, rechazo, reversión, bloqueo y cancelación | Lo mismo en desarrollo; Motor |
| Creación automática por API y por Web | Prueba de Claudio; archivada | Creación, verificación independiente y resultados reales en el ambiente local | Lo mismo en desarrollo; una respuesta con URL |
| Devolución de URL | Prueba de Claudio; pruebas unitarias sin SQL | Patrón, codificación del IdVista y ausencia de URL fuera de `Created` | Una respuesta real de la API con URL |
| Enlace «Abrir vista» | Prueba de Claudio; capturas archivadas, con respuestas simuladas en el navegador y la API detenida | El enlace aparece sólo con `Created` y URL | Que abra la vista en Motor |

La URL y los datos del ambiente de la prueba manual no se transcriben aquí. Que el alias `GestionEngine` resuelve en la base de desarrollo se infiere de que el script se aplicó; no se comprobó aparte.

## Qué quedó comprobado en local

| Prueba | Vista | Resultado |
|---|---|---|
| Fase 4, casos 01–06 | `vFase4Grilla01` y variantes | Creación y reaplicación, reversión forzada, colisión por `Nombre`, espera de bloqueo agotada, lecturas durante la espera y cancelación del cliente |
| A. Creación por la API | `vFase5Creacion01` | `Created`, verificada. La repetición devolvió `Rejected` por nombre existente, sin ejecutar nada |
| B. Timeout en el servicio | `vFase5Timeout01` | **`NO ACEPTADO`**. No se repite. Ver la sección siguiente |
| Recorrido desde la interfaz Web | `vFase5Web01` | Creada y verificada. Revisado y conforme por el usuario |

En los casos 02, 04, 05 y 06 de fase 4 se usaron variantes de prueba; no son ejecuciones del texto exacto del generador.

Último estado conocido de la base local, sin consulta nueva: 19 vistas, las 16 originales más `vFase4Grilla01`, `vFase5Creacion01` y `vFase5Web01`. En desarrollo existe además `vDesignerIATest01`, según el usuario. No limpiar ninguna por iniciativa propia.

## Resultado de la prueba B

El coordinador terminó en **`NO ACEPTADO`** por un único motivo: «el diagnóstico no confirma la ausencia de registros». Ese resultado se conserva tal cual; la decisión de cierre no lo modifica.

**Acreditado por la ejecución:**

- Timeout real del batch a los 2 s (error −2), con la sesión bloqueada en su décimo INSERT.
- Reversión: los conteos sin confirmar volvieron a 0.
- Sesión sin transacción: `@@TRANCOUNT = 0` en la misma conexión, sin necesidad del `ROLLBACK` condicional.
- Respuesta `Indeterminate`, HTTP 200, sin `viewId`. El servicio no declaró la vista creada.
- Estado independiente sin cambios, comprobado desde otra conexión después de liberar el bloqueador.

**No acreditado:**

- Que el servicio verifique por sí mismo la ausencia de registros tras un timeout. Su lectura de verificación agotó sus 15 s mientras persistía el bloqueo, y la API respondió a los 17,4 s.
- La ausencia comprobada después de liberar el bloqueo es una verificación independiente y posterior. No equivale a una verificación hecha por el servicio durante el bloqueo.

**Aceptado para el alcance local:** una verificación que no puede completarse produce `Indeterminate`.

## Cómo funciona la creación

- La API recibe únicamente el ViewSpec y genera el SQL en el servidor por el flujo existente. No acepta SQL del navegador. El generador y el preset no se modificaron.
- Condiciones comprobadas antes de ejecutar: ambiente Development, `ViewCreation:Enabled`, `CommandTimeoutSeconds` mayor que cero, usuario Windows igual a `ViewCreation:AllowedUser` y `Motor:BaseUrl` vacía o válida.
- Una creación a la vez por instancia. Conexión dedicada, un solo batch, comprobación de sesión y `ROLLBACK` condicional en la misma conexión, y verificación de conteos y valores desde otra conexión.
- Resultados: `Created`, `Rejected`, `NotAllowed`, `Busy`, `NotCreated` e `Indeterminate`. Ante `Indeterminate` no hay reintento ni se atribuye autoría.
- Cerrar el navegador no cancela el batch.

## Devolución de URL

- Patrón confirmado por el usuario: `{baseMotor}/GestionEngine/DefaultV2.aspx?Limpiarcache={IdVista codificado}&StyleVersion=V2`. Los parámetros adicionales quedan fuera.
- La base se configura en `Motor:BaseUrl`. Los valores de cada ambiente están como ejemplos comentados en `appsettings.Development.example.json`; no se repiten aquí.
- **Conexión SQL y base de Motor son una pareja** del mismo ambiente. Se cambian a la vez, sustituyendo el valor de `ConnectionStrings:GestionEngineLocal` y el de `Motor:BaseUrl`. El nombre de la clave y el alias `GestionEngine` no cambian. Cambiar esa clave mueve todos los servicios, incluidas las consultas de Copilot.
- La URL se construye en el backend sólo para `Created`, después de la verificación. El IdVista se codifica como valor de consulta.
- Sin base de Motor: `Created`, `viewId` informado, `viewUrl` nulo y una línea de diagnóstico. Es el caso del ambiente local.
- Base inválida: la creación se bloquea antes de generar o ejecutar nada, con `NotAllowed`. Se admiten `http` y `https`; se rechazan credenciales, consulta y fragmento.
- La Web muestra «Abrir vista» en «Creación de vista» sólo con `Created` y URL. Abre en pestaña nueva.
- Los ejemplos de conexión no llevan `TrustServerCertificate`: una excepción de confianza se decide aparte.

**Validado:** 732 pruebas unitarias superadas, sin SQL; compilación con 0 errores y 17 advertencias históricas; la API arranca con la configuración comentada; el enlace, con respuestas simuladas.

**No validado:** una respuesta real con URL, la apertura en Motor mediante el enlace, la conexión de la API a un ambiente con Motor (certificado, identidad Windows del proceso y alias) y cómo interpreta Motor un IdVista codificado.

## Vista previa simulada

- Componente `ViewSpecPreview.razor`, que recibe el mismo ViewSpec de la vista en progreso. No hay modelo paralelo.
- Muestra nombre, descripción, tipo de vista y la distribución de estado, fila, área y grilla, con la etiqueta «Vista previa simulada · Datos de ejemplo».
- El panel central tiene dos pestañas, «Conversación» y «Vista previa». La conversación se oculta sin desmontarse.
- Limitada a la plantilla «Consulta con grilla» y a la vista vacía soportada. Un objeto de otro tipo aparece como «todavía sin vista previa».
- La grilla usa cuatro columnas y cuatro filas fijas rotuladas «(ejemplo)»: el ViewSpec no define las columnas reales del Stored Procedure.
- No ejecuta el Stored Procedure ni consulta la base. **No sustituye ni acredita Motor.**

Revisión visual del 11 de octubre, con capturas reales a 1280, 768 y 375 px tomadas por Claudio: aspecto correcto a 1280 px; estado vacío, sustitución de la vista y conservación del borrador conformes; la grilla se desplaza dentro de su marco. El desbordamiento de página con nombres largos se corrigió en `.viewspec-name`.

Límites que siguen abiertos:

- **Tablet.** Entre 761 y 1100 px el panel central queda en 280 px, por una regla anterior a la vista previa. Requiere una decisión de UX; no se corrigió.
- **Contenido repetido.** La vista previa repite el resumen de «Vista en progreso».
- **Desplazamiento de la conversación.** Se conservó al alternar pestañas en una prueba sin mensajes reales; con una conversación real no se comprobó.
- **Vista vacía y objetos sin vista previa.** No se revisaron visualmente.
- **Revisión humana.** No consta una revisión a simple vista distinta de las capturas.

Durante la primera verificación de la vista previa un mensaje, «Mensaje de prueba», llegó por error a Copilot. Se reportó `toolInvoked: false`, sin herramientas, SQL ni escrituras. No se repitió.

## Límites conocidos

El cierre aceptado no los resuelve.

- **Creación automática fuera de local:** sin probar. Es el objeto de la reserva.
- **Designer:** la vista aplicada a mano se abrió; no se comprobó que Designer la guarde.
- **Handler HTML:** la grilla básica no incorpora el handler HTML de `vPruebaJFAL`. Queda fuera de la plantilla.
- **Concurrencia:** sin probar dos creaciones simultáneas, otras instancias de la API ni un guardado de Designer. Tampoco el interbloqueo.
- **`Busy`:** cubierto sólo por lectura del código.
- **Interfaz:** sólo el resultado «creada» se recorrió con el servicio real. Los demás resultados y el enlace se vieron simulados.
- **SQL Server 2008 R2:** sin probar. La base local es SQL Server 2022 y no consta la versión del servidor de desarrollo.
- **Pruebas unitarias:** 732 superadas, todas locales y sin SQL. Cubren decisiones puras, no la ejecución.

## Mejora pendiente, sin implementar

Reducir el tiempo máximo de la lectura de verificación, para que el servicio responda antes cuando esa lectura queda bloqueada. Es un tiempo distinto del destinado a comprobar la sesión y ejecutar el `ROLLBACK` condicional en la conexión del batch, que no debe reducirse por este motivo. Hoy ambos comparten una constante de 15 s en `ViewCreationService`.

## Reserva: comprobación integrada pendiente

Una sola comprobación, a recorrer una vez cuando se retome el ambiente. No bloquea el cierre aceptado; al completarla se levanta la reserva. El procedimiento paso a paso está en la sección 4 de `DesignerIA-Arranque-VM.md`.

1. **Ambiente coherente.** La API apunta a la base de metadata de un ambiente con Motor y `Motor:BaseUrl` es la de ese mismo ambiente. Con la creación apagada, API y GestionEngine figuran disponibles y la identidad mostrada es la prevista.
2. **Creación automática.** Con la creación habilitada sólo para ese proceso y autorización explícita para una vista nueva, se prepara una «Consulta con grilla» desde la Web y se confirma la creación una sola vez.
3. **Respuesta real.** El resultado es `Created`, con el IdVista pedido y una URL que sigue el patrón sobre la base de Motor configurada. Se conservan la respuesta y el log del servicio.
4. **Apertura.** «Abrir vista» abre esa vista en Motor y la grilla muestra datos. Se conserva una captura de la URL y del render.
5. **Registro.** Creación apagada de nuevo, ambiente local restaurado y evidencia archivada. El usuario levanta la reserva.

Ante `Indeterminate` se comprueba el estado antes de cualquier nuevo intento. La vista previa simulada, las respuestas simuladas y la prueba manual de `vDesignerIATest01` no sustituyen ninguno de estos pasos.

## Repositorio y rama

- Repositorio: `C:\Code\DesignerAI`. Rama local de trabajo: `feature/create-view-executor`. `HEAD`: `28532c2`, «feat: crea vistas verificadas con preview y URL de Motor».
- **La rama publicada es `feature/publish-phase5`.** Contiene la creación, la vista previa, la devolución de URL, `appsettings.Development.example.json` y la guía `DesignerIA-Arranque-VM.md`.
- La actualización documental del 11 de octubre —este archivo, las notas de estado de `DesignerIA-Roadmap.md` y `DesignerIA-Contexto.md` y dos correcciones en `DesignerIA-Arranque-VM.md`— está en el árbol de trabajo de la rama local, sin confirmar ni publicar.
- `appsettings.Development.json` no está en Git y mantiene la conexión local, sin base de Motor y con la creación apagada.
- **Al comenzar, recontrastar rama, `HEAD` y árbol.** No dar por válido este checkpoint si difieren, y no descartar cambios.

## Evidencia local

En `artifacts/`, carpeta ignorada por Git. Puede no existir en otro equipo. No contiene la evidencia manual del usuario.

- `artifacts/fase4-resumen-casos-01-06.md`: resumen de los seis casos de fase 4.
- `artifacts/fase5-crear-vista/00-LEEME.md`: pruebas A y B y recorrido Web.
- `artifacts/fase5-preview-revision-visual/00-INFORME.md`: revisión visual de la vista previa, con capturas.
- `artifacts/fase5-nombre-largo-correccion/00-INFORME.md`: corrección del desbordamiento por nombres largos.
- `artifacts/fase5-url-motor/00-INFORME.md`: devolución de URL, validaciones y activación de un ambiente.

## Cómo habilitar la creación para una prueba

Con una instancia propia de la API y variables que sólo afectan a ese proceso:

```text
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://127.0.0.1:5299
ViewCreation__Enabled=true
```

No cambiar los archivos de configuración para habilitarla.

## Autorizaciones

Este checkpoint no autoriza nada. Crear vistas, ejecutar SQL de escritura, limpiar datos, cambiar de ambiente, hacer commit, publicar, integrar, agregar dependencias o desplegar dependen del encargo explícito del usuario y de `AGENTS.md`.

Lectura de archivos y Git de lectura pueden hacerse sin preguntar. Las consultas SQL de lectura requieren que el usuario autorice servidor, base y autenticación para el encargo.
