using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DesignerIA.Api.Services.Knowledge.Extractors;

/// <summary>
/// Extractor para libros Excel (.xlsx) mediante DocumentFormat.OpenXml. Cada hoja
/// se convierte en una sola sección (heading = nombre de la hoja), concatenando el
/// texto de sus celdas fila por fila. No interpreta fórmulas ni formato.
/// </summary>
public static class ExcelKnowledgeExtractor
{
    public static IReadOnlyList<KnowledgeSection> Extract(string filePath)
    {
        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return Array.Empty<KnowledgeSection>();
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sourceDate = TryGetLastWriteDate(filePath);
        var sections = new List<KnowledgeSection>();

        foreach (var sheet in workbookPart.Workbook.Descendants<Sheet>())
        {
            if (sheet.Id?.Value is null || workbookPart.GetPartById(sheet.Id.Value!) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            var lines = new List<string>();
            foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
            {
                var cellsText = row.Elements<Cell>()
                    .Select(cell => GetCellText(cell, sharedStrings))
                    .Where(text => !string.IsNullOrWhiteSpace(text));

                var line = string.Join(" | ", cellsText);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            var content = string.Join(Environment.NewLine, lines).Trim();
            if (content.Length > 0)
            {
                sections.Add(new KnowledgeSection(sheet.Name?.Value, content, sourceDate));
            }
        }

        return sections;
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.InnerText;
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings is not null
            && int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.ChildElements.Count)
        {
            return sharedStrings.ChildElements[index].InnerText;
        }

        return value;
    }

    private static string? TryGetLastWriteDate(string filePath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(filePath).ToString("yyyy-MM-dd");
        }
        catch
        {
            return null;
        }
    }
}
