using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewTemplates;

public static class GridQueryTemplate
{
    public static ViewSpec Create(GridQueryTemplateRequest request) => new(
        request.Name,
        request.Description,
        "Estado01",
        [new ViewSpecState("Estado01", [new ViewSpecRow(
            [new ViewSpecArea("Grilla", request.StoredProcedure, request.ConnectionAlias, [],
                "Area01", "Grilla01", GridConfigurationPresets.GridQuery)], "Fila01")])],
        "go");
}
