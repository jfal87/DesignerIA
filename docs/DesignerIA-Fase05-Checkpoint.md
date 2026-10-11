# DesignerIA — Checkpoint de «creación local comprobada»

Fecha: 11 de octubre de 2026. Contexto breve para los chats nuevos de Claudio y del revisor. No requiere transportar conversaciones anteriores.

## Estado

- **Metadata local comprobada (fase 4).** Seis casos ejecutados y revisados en `LAPTOP-GM0SHH26 / GestionEngine`: creación y reaplicación, reversión forzada, colisión por `Nombre`, espera de bloqueo agotada, lecturas durante la espera y cancelación del cliente.
- **Creación local comprobada (fase 5, parcial).** DesignerIA crea la vista desde la API y desde la interfaz Web, y la verifica desde otra conexión. Comprobado sólo en el ambiente local.
- **Vista previa simulada entregada.** Revisión de código conforme por el usuario. Validada sólo por DOM, sin capturas ni revisión visual humana. Ver «Vista previa simulada».
- **Entrega local cerrada.** Quedan pendientes Motor, la URL y el cierre formal de las fases 4 y 5.
- **Motor sin comprobar.** No hay Motor disponible ni URL base definida. Ni la fase 4 ni la fase 5 quedan cerradas: la fase 5 exige además la URL.
- **La creación está apagada** en `appsettings.json` y en `appsettings.Development.json`. Sólo se habilitó por variable de entorno en procesos propios de prueba.

## Qué quedó comprobado

| Prueba | Vista | Resultado |
|---|---|---|
| A. Creación por la API | `vFase5Creacion01` | `Created`, verificada. La repetición devolvió `Rejected` por nombre existente, sin ejecutar nada |
| B. Timeout en el servicio | `vFase5Timeout01` | **`NO ACEPTADO`**. No se repite. Ver la sección siguiente |
| Recorrido desde la interfaz Web | `vFase5Web01` | Creada y verificada. Revisado y conforme por el usuario. La repetición opcional no se ejecutó |

La base local tiene 19 vistas: las 16 originales más `vFase4Grilla01`, `vFase5Creacion01` y `vFase5Web01`. No hay restos de las pruebas fallidas ni se hizo limpieza.

## Resultado de la prueba B

El coordinador terminó en **`NO ACEPTADO`** por un único motivo: «el diagnóstico no confirma la ausencia de registros». Ese resultado se conserva tal cual.

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
- Condiciones comprobadas en la API antes de ejecutar: ambiente Development, `ViewCreation:Enabled`, `CommandTimeoutSeconds` mayor que cero y usuario Windows igual a `ViewCreation:AllowedUser`.
- Una creación a la vez por instancia. Conexión dedicada, un solo batch, comprobación de sesión y `ROLLBACK` condicional en la misma conexión, y verificación de conteos y valores desde otra conexión.
- Resultados: `Created`, `Rejected`, `NotAllowed`, `Busy`, `NotCreated` e `Indeterminate`. Ante `Indeterminate` no hay reintento ni se atribuye autoría.
- Cerrar el navegador no cancela el batch.
- La creación no devuelve URL.

## Vista previa simulada

### Alcance implementado

- Componente `ViewSpecPreview.razor`, que recibe el mismo ViewSpec de la vista en progreso. No hay modelo paralelo.
- Muestra nombre, descripción, tipo de vista y la distribución de estado, fila, área y grilla, con la etiqueta visible «Vista previa simulada · Datos de ejemplo».
- El panel central tiene dos pestañas, «Conversación» y «Vista previa». La conversación se oculta sin desmontarse.
- Se actualiza al sustituir la vista y muestra un estado vacío cuando no hay ninguna preparada.
- Limitada a la plantilla «Consulta con grilla» y a la vista vacía soportada, que se muestra como un estado sin filas. Un objeto de otro tipo aparece como «todavía sin vista previa».
- Sólo cambia la Web. No ejecuta el Stored Procedure ni añade consultas SQL, escrituras, dependencias o cambios del backend. El flujo de generación y creación no cambió.

### Datos de ejemplo

El ViewSpec no define las columnas reales del Stored Procedure. La grilla usa cuatro columnas y cuatro filas fijas, inventadas en el componente y rotuladas «(ejemplo)», con una nota bajo la grilla que lo aclara. No proceden de la base.

### Validaciones

Compilación sin errores y 709 pruebas superadas; ninguna prueba cubre la vista previa.

El resto se reportó **por texto de la página y medidas del DOM, sin capturas de pantalla ni revisión visual humana**: el panel del navegador estaba oculto y las capturas no respondieron.

- Preparación de una vista con el formulario existente.
- Cambio de vista, también con la pestaña de vista previa abierta.
- Estado vacío, sin vista preparada y tras «Nueva conversación».
- Vista vacía, con una respuesta simulada en el navegador y no por el flujo real: el formulario sólo prepara la plantilla de grilla.
- Alternancia de pestañas: se conservan los mensajes y el borrador.
- Anchos de 1280, 768 y 375 px sin desbordamiento horizontal de la página; la grilla se desplaza dentro de su marco. Panel central de 649, 280 y 319 px.
- Consola sin errores.

En 375 px la vista previa ensanchaba la página. Se corrigió con `min-width: 0` en `.chat-panel`, una regla que ya existía.

### Límites de la vista previa

- **Sin revisión visual.** Nadie comprobó a simple vista el aspecto, el espaciado ni la legibilidad.
- **Espacio estrecho en tablet.** En 768 px el panel central queda en 280 px, porque la regla que oculta el panel izquierdo por debajo de 1100 px queda anulada por otra posterior. Es anterior a esta entrega y no se corrigió: ocultar ese panel quitaría «+ Nueva vista» y «+ Nueva conversación», y requiere una decisión de UX.
- **Posición de desplazamiento no comprobada.** Al alternar pestañas se verificaron mensajes y borrador, no que la conversación conserve su desplazamiento.

### Solicitud accidental a Copilot

Durante la verificación se interceptó `/api/chat`, pero la ruta real es `/api/copilot/chat`. Un mensaje, «Mensaje de prueba», llegó a la API y a Copilot. La respuesta pidió más detalle y llegó con `toolInvoked: false`: se reporta sin herramientas, sin SQL y sin escrituras. No se repitió.

## Límites

- **Motor:** sin render comprobado, sin URL. No inventar un host ni preparar despliegues o aplicaciones en otra base.
- **Concurrencia:** sin probar dos creaciones simultáneas, otras instancias de la API ni un guardado de Designer. Tampoco el interbloqueo.
- **`Busy`:** cubierto sólo por lectura del código.
- **Interfaz:** sólo el resultado «creada» se recorrió de verdad. Los demás resultados se vieron simulados.
- **SQL Server 2008 R2:** sin probar. La base local es SQL Server 2022.
- **Designer:** sin comprobar que abra y guarde las vistas creadas.
- **Pruebas unitarias:** 709 superadas, todas locales y sin SQL. Cubren las decisiones puras de la creación, no la ejecución.

## Mejora pendiente, sin implementar

Reducir el tiempo máximo de la lectura de verificación, para que el servicio responda antes cuando esa lectura queda bloqueada. Es un tiempo distinto del destinado a comprobar la sesión y ejecutar el `ROLLBACK` condicional en la conexión del batch, que no debe reducirse por este motivo. Hoy ambos comparten una constante de 15 s en `ViewCreationService`.

## Repositorio y rama

- Repositorio: `C:\Code\DesignerAI`. Rama: `feature/create-view-executor`. `HEAD`: `a8211a5`.
- **Nada está confirmado en Git.** La creación y la vista previa simulada están en el árbol de trabajo:
  - modificados: `ViewScriptController.cs`, `Program.cs`, `appsettings.json`, `Home.razor`, `_Imports.razor` y `app.css`;
  - nuevos: `ViewCreationService.cs`, `ViewCreationVerifier.cs`, `ViewCreationResult.cs`, `ViewCreationTests.cs`, `Components/ViewSpecPreview.razor` y este archivo.
- `appsettings.Development.json` no está en Git y contiene el usuario autorizado, con la creación apagada.
- **Al comenzar, recontrastar rama, `HEAD` y árbol.** No dar por válido este checkpoint si difieren, y no descartar cambios.

La vista previa simulada se añadió después de las pruebas de creación. No altera lo comprobado sobre la creación ni está cubierta por esas pruebas.

## Evidencia local

En `artifacts/`, carpeta ignorada por Git. Puede no existir en otro equipo.

- `artifacts/fase4-resumen-casos-01-06.md`: resumen de los seis casos de fase 4.
- `artifacts/fase5-crear-vista/00-LEEME.md`: procedimiento, huellas y resultados de las pruebas A y B y del recorrido Web.

## Cómo habilitar la creación para una prueba

Con una instancia propia de la API y variables que sólo afectan a ese proceso:

```text
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://127.0.0.1:5299
ViewCreation__Enabled=true
```

No cambiar los archivos de configuración para habilitarla.

## Autorizaciones

Este checkpoint no autoriza nada. Crear vistas, ejecutar SQL de escritura, limpiar datos, hacer commit, publicar, integrar, agregar dependencias o desplegar dependen del encargo explícito del usuario y de `AGENTS.md`.

Lectura de archivos y Git de lectura pueden hacerse sin preguntar. Las consultas SQL de lectura requieren que el usuario autorice servidor, base y autenticación para el encargo.
