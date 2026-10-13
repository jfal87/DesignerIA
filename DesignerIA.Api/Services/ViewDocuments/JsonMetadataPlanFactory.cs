using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewDocuments;

/// <summary>
/// Convierte una definición efectiva ya validada, con su catálogo resuelto, en el
/// plan físico que comparten todas las entradas de creación. Aquí viven las reglas
/// de persistencia de la entrada por documento, tomadas de cómo guarda Designer:
/// - fila «estado + F0» y objeto «área + _Obj»;
/// - los web controls se escriben una sola vez, en el estado inicial, y cada estado
///   lista en su configuración los que tiene activos;
/// - la fila LMFiltros sólo en el estado inicial; la vista vacía conserva su forma
///   heredada, sin ella.
/// No decide nada que el documento o el catálogo no hayan resuelto.
/// </summary>
public static class JsonMetadataPlanFactory
{
    // dbo.TiposWebControl: Id 4, Tipo «ButtonList». El script comprueba ambos en la base.
    private const int ButtonListTypeId = 4;
    private const string ButtonListTypeName = "ButtonList";

    public static string RowName(string stateName) => stateName + "F0";

    public static string ObjectName(string areaName) => areaName + "_Obj";

    /// <param name="grids">Resultado del catálogo de la grilla de cada estado, por posición; null si el estado no tiene grilla.</param>
    public static ViewCreationPlan Create(JsonMetadataDocument document, IReadOnlyList<ViewCreationPreflightResult?> grids,
        List<JsonMetadataAnnotation> annotations)
    {
        var view = document.View!;
        if (!string.IsNullOrEmpty(view.Js))
        {
            annotations.Add(new("view.js", JsonMetadataOrigin.Derivado,
                "Se guarda en Vistas.JsCode con la codificación de Designer; Motor la revierte al cargar la vista. ArchivoJs permanece vacío."));
        }
        var states = document.States!;
        var definitions = document.Controls ?? [];
        var blocks = states.Select((state, index) => CreateState(state, index, definitions, grids[index], annotations)).ToList();
        var initialState = states.First(state => state.IsDefault == true).Name!;
        var controls = definitions.Select((control, index) => CreateControl(control, index, initialState, annotations)).ToList();
        return new ViewCreationPlan(
            new ViewCreationPlanView(view.Id!, view.Description!, view.Type!, ViewJavaScriptMetadata.Encode(view.Js)),
            blocks[0].State, blocks[0].StateConfiguration, blocks[0].Row, blocks[0].Grid,
            blocks.Count > 1 ? blocks.Skip(1).ToList() : null,
            controls.Count > 0 ? controls : null);
    }

    private static ViewCreationPlanStateBlock CreateState(JsonMetadataState state, int index, IReadOnlyList<JsonMetadataControl> definitions,
        ViewCreationPreflightResult? catalog, List<JsonMetadataAnnotation> annotations)
    {
        var stateName = state.Name!;
        var statePath = $"states[{index}]";
        var isDefault = state.IsDefault == true;
        var rowName = RowName(stateName);
        // Como Designer: los controles activos, por nombre y en el orden en que se definen en la vista.
        var active = state.Controls ?? [];
        var filters = string.Join(",", definitions.Select(control => control.Name!)
            .Where(name => active.Contains(name, StringComparer.OrdinalIgnoreCase)));
        var activeNote = filters.Length == 0 ? "sin web controls activos" : $"web controls activos «{filters}»";

        var area = state.Rows is { Count: > 0 } rows ? rows[0].Areas![0] : null;
        ViewCreationPlanGrid? grid = null;
        if (area is null)
        {
            annotations.Add(new($"{statePath}.rows", JsonMetadataOrigin.Derivado,
                $"Sin filas lógicas. Se escribe una fila física «{rowName}», que Motor necesita para encontrar el estado."));
            annotations.Add(new(statePath, JsonMetadataOrigin.Derivado,
                $"Configuración de estado: {activeNote}. Sin fila LMFiltros: forma heredada de la vista vacía, pendiente de comprobar en Designer y Motor."));
        }
        else
        {
            var data = area.Object!.Data!;
            var objectName = ObjectName(area.Name!);
            grid = new ViewCreationPlanGrid(area.Name!, objectName, catalog!.CanonicalStoredProcedure!, data.Connection!,
                GridConfigurationPresets.GridQueryConfiguration, isDefault ? ViewCreationPlanFactory.DefaultLeftMenu : null,
                catalog.Parameters is { Count: > 0 } parameters
                    ? parameters.Select(parameter => new ViewCreationPlanParameter(parameter.Name, parameter.Type,
                        ViewCreationPreflightService.UsesQuotes(parameter.Type), parameter.DefaultValue)).ToList()
                    : null);
            annotations.Add(new($"{statePath}.rows[0]", JsonMetadataOrigin.Derivado, $"Nombre de fila «{rowName}»."));
            annotations.Add(new($"{statePath}.rows[0].areas[0].object", JsonMetadataOrigin.Derivado,
                $"Nombre de objeto «{objectName}» y configuración de grilla de referencia de la versión {JsonMetadataExpander.SchemaVersion}."));
            annotations.Add(new(statePath, JsonMetadataOrigin.Derivado,
                $"Configuración de estado: {activeNote}." + (isDefault
                    ? " Fila LMFiltros vacía, por ser el estado inicial."
                    : " Sin fila LMFiltros: Designer sólo la escribe en el estado inicial.")));
        }
        return new(new(stateName, isDefault), new(ViewCreationPlanFactory.StateConfiguration(filters)), new(rowName, Width: 0, Height: 0), grid);
    }

    private static ViewCreationPlanControl CreateControl(JsonMetadataControl control, int index, string initialState,
        List<JsonMetadataAnnotation> annotations)
    {
        var target = control.Actions![0].Parameters![0];
        // Sin comodín, Designer marca como predeterminada la primera opción.
        var items = control.Items!.Select((item, itemIndex) => new ViewCreationPlanControlItem(item.Value!, item.Text!, itemIndex == 0)).ToList();
        annotations.Add(new($"controls[{index}]", JsonMetadataOrigin.Derivado,
            $"Se escribe una sola vez, en el estado inicial «{initialState}»: lista de botones, orden {index}, sin título ni comodín, " +
            "primera opción predeterminada, visible en ambos ambientes y habilitado."));
        return new(control.Name!, ButtonListTypeId, ButtonListTypeName, index, items, NavigationConfiguration(target));
    }

    /// <summary>
    /// dbo.ConfiguracionesFiltros.Configuracion de una lista de botones que cambia de
    /// estado, con el texto exacto que escribe Designer.
    /// </summary>
    public static string NavigationConfiguration(string targetState) =>
        "{\"actions\":{\"" + JsonMetadataValidator.ChangeStateAction + "\":[\"" + targetState + "\"]}, \"SufijoBoton\":\"false\", \"visibleEn\":\"A\", \"enabled\":\"true\"}";
}
