# Vista

**Significado.** La vista es la unidad que Motor abre por su identificador. Un documento describe una vista **nueva**: el identificador no recupera, clona ni reconstruye una existente.

**Ámbito.** `view`, una por documento.

## Propiedades

| Propiedad | Omitirla significa | Default | Clase |
|---|---|---|---|
| `id` | No se puede omitir | — | Regla: `Vistas.IdVista` = `Vistas.Nombre` |
| `description` | Usar el id | `view.id` | Convención de DesignerIA (heredada) |
| `type` | Tipo por defecto | `site` | Default de Designer para una vista nueva |
| `usesSecurity` | Sin seguridad | `false` | Default de Designer |
| `js` | La vista no tiene JS | — | Texto opcional; se guarda en `Vistas.JsCode` con codificación de Designer |

Lo asigna el backend y no es propiedad: versión `1.0` en `VersionActual` y `VistaVersionado`, `IdOwner` 1, `IdHistoricoCambio` 1, `ArchivoJs` y `Frente` vacíos, usuario y fechas de auditoría.

Restricciones de DesignerIA: `id` como identificador ASCII de hasta 250 caracteres; descripción de hasta 350; si el `id` ya existe por `IdVista` o por `Nombre`, la creación se rechaza y nunca se llama a `eliminarVista`.

## Ejemplo

```json
{ "schemaVersion": "1.0", "view": { "id": "vNueva" } }
```

Es una vista vacía: `site`, un estado `Pantalla` sin contenido. Ver [estado.md](estado.md).

## Evidencia

- Designer: `EngineUpdater.GetViewInsertQry`; `Designer.js` `reset`.
- Motor: `recuperarEstadoDefault` devuelve `UsaSeguridad`, `jsCode`, `VistaVersionado` y `Descripcion`; `Default.aspx.cs` pasa a minúsculas el id de la URL.
- Exports: `go` en `vpruebaJFAL` y `vPreciosSemanales_CDA`; `site` con seguridad en `AppPrecios`.

## Cobertura y huecos

- Creable: `site` vacía y `go` con una grilla. `site` con grilla se rechaza: no hay caso comprobado.
- Pendiente: efecto de `TipoVista` y de `UsaSeguridad` en Motor.
- Fuera: historial de versiones (`VistaVersionado`), favoritos y URL cortas.
- JS: DesignerIA conserva el texto lógico en JSONMetadata y lo guarda como Designer (`"` → `\u0022`, `'` → `\u0027`, `null` → `\u0000`). Motor revierte esas secuencias al registrar el script. La verificación de creación exige que `Vistas.JsCode` coincida con el plan. Ver [javascript.md](javascript.md).
