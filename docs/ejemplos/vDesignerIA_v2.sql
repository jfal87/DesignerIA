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
               WHERE IdVista = N'vDesignerIA_v2' OR Nombre = N'vDesignerIA_v2')
        RAISERROR(N'La vista ya existe; el script no realiza cambios.', 16, 1);

    DECLARE @IdStringConnection int, @AliasCount int;
    SELECT @AliasCount = COUNT(*), @IdStringConnection = MIN(IdStringConnection)
    FROM dbo.StringsConnection WHERE StringConnection = N'GestionEngine';
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
    DECLARE @IDX_WebControl int, @IDX_WebControlItem int;
    SELECT @IDX_WebControl = ISNULL(MAX(IdWebControl), 1) + 1 FROM dbo.WebControls WITH (UPDLOCK, HOLDLOCK);
    SELECT @IDX_WebControlItem = ISNULL(MAX(IdItemWebControl), 1) + 1 FROM dbo.ItemsWebControl WITH (UPDLOCK, HOLDLOCK);
    IF NOT EXISTS (SELECT 1 FROM dbo.TiposWebControl WHERE IdTipoWebControl = 4 AND Tipo = N'ButtonList')
        RAISERROR(N'El catálogo de tipos de web control de esta base no es el esperado.', 16, 5);

    INSERT INTO dbo.Vistas (IdOwner, IdHistoricoCambio, Nombre, Descripcion, FechaCreacion, VersionActual, ArchivoJs, IdVista, TipoVista, UsaSeguridad, User_Create, User_Update, F_Create, F_Update, IdStringsConnectionMyVision, JsCode, Frente, VistaVersionado)
    VALUES (1, 1, N'vDesignerIA_v2', N'Demo DesignerIA: navegacion y JavaScript', GETDATE(), N'1.0', N'', N'vDesignerIA_v2', N'go', 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL, NULL, N'var contador = 0;

function Inicializar() {
    var indicador = document.getElementById(\u0022designeria-demo-js\u0022);
    if (!indicador) {
        indicador = document.createElement(\u0022div\u0022);
        indicador.id = \u0022designeria-demo-js\u0022;
        indicador.style.cssText = \u0022position:fixed;bottom:18px;right:18px;z-index:10000;padding:10px 16px;background:#e8f4ed;color:#236b3d;border:1px solid #b7d9c4;border-radius:6px;font:14px Arial;\u0022;
        document.body.appendChild(indicador);
    }
    indicador.textContent = \u0022DesignerIA | JS activo | Clics: \u0022 + contador;
}

function clickEnWC(tipoObjeto, nombre, valor) {
    contador += 1;
    Inicializar();
}

if (document.readyState === \u0022loading\u0022) {
    document.addEventListener(\u0022DOMContentLoaded\u0022, Inicializar);
} else {
    Inicializar();
}
', N'', N'1.0');

    INSERT INTO dbo.Estados (IdEstado, [Default], Descripcion, IdVista, ArchivoJs, LeftMenuFijo, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_Estado, 1, N'Principal', N'vDesignerIA_v2', N'', 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.Filas (IdFila, IdEstado, Nombre, Ancho, Altura, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_Fila, @IDX_Estado, N'PrincipalF0', 0, 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.Areas (IdArea, IdFila, Nombre, Ancho, Altura, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_Area, @IDX_Fila, N'AreaPrincipal', 0, 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ExtraccionesDatos (IdExtraccionDato, Descripcion, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_ExtraccionDato, N'[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]', N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.StoredProcedure (IdStoredProcedure, Nombre, Descripcion, OrdenEjecucion, IdStringConnection, IdExtraccionDato, IdTipoEjecucionSP, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_StoredProcedure, '[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]', N'', 0, @IdStringConnection, @IDX_ExtraccionDato, 1, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ObjetosDeArea (IdObjetoDeArea, IdArea, IdExtraccionDato, IdTipoObjetoDeArea, Nombre, UsaPaginador, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_ObjetoArea, @IDX_Area, @IDX_ExtraccionDato, 2, N'AreaPrincipal_Obj', 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ConfiguracionesObjetoDeArea (Id, IdObjetoDeArea, Configuracion, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_ObjetoAreaConfig, @IDX_ObjetoArea, N'{"TituloArea":"[]","BotonAyuda":"[]","Render":"1","RelacionLM":"","CantRegistrosLoopExcel":"20","MaximoTextoAncho":"50","ColumnTotalsRenderMode":"RENDER_BK_TIT","ExportarExcel":"1","Renglones":"50","columnasExcelDescripcion":"true","IdColumnaCodigo":"0","IdColumnaDescripcion":"1","Decimales":"0","ExpandedRowLevels":"3","SeparadorDecimal":",","SeparadorMiles":".","TitulosExcel":"","AnchoColumnas":"150","FixedColumnsMobile":"","RowGroupSeparator":"false","ColumnGroupSeparator":"false","EnableHeader":"true","EnableReference":"true","EnableBody":"true","TitulosFijos":"true","AggregationTypes":"1","ColumnAggregationType":"1","MostrarCeros":"true","IndizarPorColumna":"true","VisualizarCampos":"false","ResaltarRegistros":"false","BotonesArea":{"Boton1":{"ID":"Expandir","ToolTip":"Expandir"},"Boton2":{"ID":"Excel Sin Formato","ToolTip":"Excel Sin Formato"}},"Hashtag":"", "UnidadAltoFila": "817"}', N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.LeftMenus (IdLeftMenu, IdEstado, Nombre, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_LeftMenu, @IDX_Estado, N'LMFiltros', N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.WebControls (IdWebControl, IdTipoWebControl, IdEstado, IdExtraccionDato, Nombre, Titulo, ValoresComodin, Orden, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_WebControl, 4, @IDX_Estado, -1, N'wcIrDetalle', N'', N'', 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ItemsWebControl (IdItemWebControl, IdWebControl, ItemId, ItemDescripcion, ItemDefault, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_WebControlItem, @IDX_WebControl, N'detalle', N'Ir a detalle', 1, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ConfiguracionesFiltros (IdWebControl, Configuracion, User_Create, F_Update)
    VALUES (@IDX_WebControl, N'{"actions":{"cambiarEstado":["Detalle"]}, "SufijoBoton":"false", "visibleEn":"A", "enabled":"true"}', N'LAPTOP-GM0SHH26\ASUS_JFAL', GETDATE());

    SET @IDX_WebControl = @IDX_WebControl + 1;
    INSERT INTO dbo.WebControls (IdWebControl, IdTipoWebControl, IdEstado, IdExtraccionDato, Nombre, Titulo, ValoresComodin, Orden, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_WebControl, 4, @IDX_Estado, -1, N'wcVolver', N'', N'', 1, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    SET @IDX_WebControlItem = @IDX_WebControlItem + 1;
    INSERT INTO dbo.ItemsWebControl (IdItemWebControl, IdWebControl, ItemId, ItemDescripcion, ItemDefault, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_WebControlItem, @IDX_WebControl, N'volver', N'Volver', 1, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ConfiguracionesFiltros (IdWebControl, Configuracion, User_Create, F_Update)
    VALUES (@IDX_WebControl, N'{"actions":{"cambiarEstado":["Principal"]}, "SufijoBoton":"false", "visibleEn":"A", "enabled":"true"}', N'LAPTOP-GM0SHH26\ASUS_JFAL', GETDATE());

    INSERT INTO dbo.ConfiguracionesEstados (IdEstado, Configuracion, User_Create, F_Update)
    VALUES (@IDX_Estado, N'{"filters":"wcIrDetalle","leftMenus":"","isPopUp":false,"popUp":{},"idLayout":"00"}', N'LAPTOP-GM0SHH26\ASUS_JFAL', GETDATE());

    SET @IDX_Estado = @IDX_Estado + 1;
    INSERT INTO dbo.Estados (IdEstado, [Default], Descripcion, IdVista, ArchivoJs, LeftMenuFijo, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_Estado, 0, N'Detalle', N'vDesignerIA_v2', N'', 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    SET @IDX_Fila = @IDX_Fila + 1;
    INSERT INTO dbo.Filas (IdFila, IdEstado, Nombre, Ancho, Altura, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_Fila, @IDX_Estado, N'DetalleF0', 0, 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    SELECT @AliasCount = COUNT(*), @IdStringConnection = MIN(IdStringConnection)
    FROM dbo.StringsConnection WHERE StringConnection = N'GestionEngine';
    IF @AliasCount <> 1
        RAISERROR(N'El alias de conexión no existe o es ambiguo en esta base.', 16, 2);

    SET @IDX_Area = @IDX_Area + 1;
    SET @IDX_ExtraccionDato = @IDX_ExtraccionDato + 1;
    SET @IDX_StoredProcedure = @IDX_StoredProcedure + 1;
    SET @IDX_ObjetoArea = @IDX_ObjetoArea + 1;
    SET @IDX_ObjetoAreaConfig = @IDX_ObjetoAreaConfig + 1;
    INSERT INTO dbo.Areas (IdArea, IdFila, Nombre, Ancho, Altura, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_Area, @IDX_Fila, N'AreaDetalle', 0, 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ExtraccionesDatos (IdExtraccionDato, Descripcion, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_ExtraccionDato, N'[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]', N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.StoredProcedure (IdStoredProcedure, Nombre, Descripcion, OrdenEjecucion, IdStringConnection, IdExtraccionDato, IdTipoEjecucionSP, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_StoredProcedure, '[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]', N'', 0, @IdStringConnection, @IDX_ExtraccionDato, 1, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ObjetosDeArea (IdObjetoDeArea, IdArea, IdExtraccionDato, IdTipoObjetoDeArea, Nombre, UsaPaginador, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_ObjetoArea, @IDX_Area, @IDX_ExtraccionDato, 2, N'AreaDetalle_Obj', 0, N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ConfiguracionesObjetoDeArea (Id, IdObjetoDeArea, Configuracion, User_Create, User_Update, F_Create, F_Update)
    VALUES (@IDX_ObjetoAreaConfig, @IDX_ObjetoArea, N'{"TituloArea":"[]","BotonAyuda":"[]","Render":"1","RelacionLM":"","CantRegistrosLoopExcel":"20","MaximoTextoAncho":"50","ColumnTotalsRenderMode":"RENDER_BK_TIT","ExportarExcel":"1","Renglones":"50","columnasExcelDescripcion":"true","IdColumnaCodigo":"0","IdColumnaDescripcion":"1","Decimales":"0","ExpandedRowLevels":"3","SeparadorDecimal":",","SeparadorMiles":".","TitulosExcel":"","AnchoColumnas":"150","FixedColumnsMobile":"","RowGroupSeparator":"false","ColumnGroupSeparator":"false","EnableHeader":"true","EnableReference":"true","EnableBody":"true","TitulosFijos":"true","AggregationTypes":"1","ColumnAggregationType":"1","MostrarCeros":"true","IndizarPorColumna":"true","VisualizarCampos":"false","ResaltarRegistros":"false","BotonesArea":{"Boton1":{"ID":"Expandir","ToolTip":"Expandir"},"Boton2":{"ID":"Excel Sin Formato","ToolTip":"Excel Sin Formato"}},"Hashtag":"", "UnidadAltoFila": "817"}', N'LAPTOP-GM0SHH26\ASUS_JFAL', NULL, GETDATE(), NULL);

    INSERT INTO dbo.ConfiguracionesEstados (IdEstado, Configuracion, User_Create, F_Update)
    VALUES (@IDX_Estado, N'{"filters":"wcVolver","leftMenus":"","isPopUp":false,"popUp":{},"idLayout":"00"}', N'LAPTOP-GM0SHH26\ASUS_JFAL', GETDATE());

    IF (SELECT COUNT(*) FROM dbo.Filas WHERE IdFila = @IDX_Fila) <> 1
        RAISERROR(N'IdFila duplicado; no se realizan cambios.', 16, 3);
    IF (SELECT COUNT(*) FROM dbo.Vistas WHERE IdVista = N'vDesignerIA_v2' OR Nombre = N'vDesignerIA_v2') <> 1
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
