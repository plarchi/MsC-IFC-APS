using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;

[ApiController]
[Route("api/[controller]")]
public class ModelsController : ControllerBase
{
    public record BucketObject(string name, string urn);
    public record RevisedIfcObject(string name);
    public record SqlDatabaseCobieRow(string Name, int RowCount, int CobieCount);
    public record SqlDatabaseNotebookResponse(string Title, IEnumerable<SqlDatabaseCobieRow> Rows, string Summary);
    public record RevisedComparisonRow(
        string ExistingModelName,
        string EditedName,
        string PropertyCategory,
        string PropertyDisplayName,
        int Count);

    public record CobieImplementationRow(
        string ItemType,
        string IfcType,
        string TotalItemsCount,
        string Name,
        string CobieValue);

    public record RevisedFlipNameCount(string Name, int Count);

    public record RevisedFlipSummaryGroup(
        string Label,
        string IfcType,
        int Count,
        IReadOnlyList<RevisedFlipNameCount> Names);

    public record RevisedFlipSummaryResponse(
        int TotalCount,
        IReadOnlyList<RevisedFlipSummaryGroup> Groups);

    public class ExtractProperty
    {
        public string DisplayName { get; set; }
        public string DisplayValue { get; set; }
        public string Category { get; set; }
    }

    public class ExtractDataRequest
    {
        public string Urn { get; set; }
        public int DbId { get; set; }
        public string Name { get; set; }
        public string ExternalId { get; set; }
        public List<ExtractProperty> Properties { get; set; } = new();
    }

    public class ExportElementPropertiesRequest
    {
        public string Urn { get; set; }
        public int DbId { get; set; }
        public string ViewGuid { get; set; }
    }

    public record ExportElementPropertiesResponse(string FileName, string Folder, int PropertyCount, string ElementName);

    public class ExportWholeModelPropertiesRequest
    {
        public string Urn { get; set; }
        public string ViewGuid { get; set; }
    }

    public record ExportWholeModelPropertiesResponse(string FileName, string Folder, int ElementCount, string ModelName);

    private const int WholeModelFallbackConcurrency = 6;

    private readonly APS _aps;
    private readonly IWebHostEnvironment _env;

    public ModelsController(APS aps, IWebHostEnvironment env)
    {
        _aps = aps;
        _env = env;
    }

    [HttpGet("sql-database-cobie")]
    public IActionResult GetSqlDatabaseCobieNotebookContent()
    {
        if (!TryReadSqlDatabaseCobieNotebookOutputs(out var htmlTable, out var summaryText, out _, out var errorMessage))
        {
            return NotFound(errorMessage);
        }

        if (!TryReadSqlDatabaseCobieRows(out var rows, out var sqlErrorMessage))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, sqlErrorMessage);
        }

        return Ok(new SqlDatabaseNotebookResponse(
            Title: "IFCAllData-COBie Bubble Chart by Name",
            Rows: rows,
            Summary: summaryText ?? string.Empty));
    }

    [HttpGet("sql-database-cobie-chart")]
    public IActionResult GetSqlDatabaseCobieNotebookChart()
    {
        if (!TryReadSqlDatabaseCobieNotebookOutputs(out _, out _, out var chartBytes, out var errorMessage))
        {
            return NotFound(errorMessage);
        }

        return File(chartBytes!, "image/png");
    }

    [HttpGet()]
    public async Task<IEnumerable<BucketObject>> GetModels()
    {
        var objects = await _aps.GetObjects();
        return from o in objects
               select new BucketObject(o.ObjectKey, APS.Base64Encode(o.ObjectId));
    }

    [HttpGet("revised-models")]
    public ActionResult<IEnumerable<RevisedIfcObject>> GetRevisedModels()
    {
        var revisedFolder = Path.Combine(_env.ContentRootPath, "Revised_IFC");
        if (!Directory.Exists(revisedFolder))
        {
            return Ok(Array.Empty<RevisedIfcObject>());
        }

        var files = Directory
            .EnumerateFiles(revisedFolder, "*.ifc", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new RevisedIfcObject(name!))
            .ToArray();

        return Ok(files);
    }

    // GET api/models/revised-comparison/{fileName}
    // Returns comparison table rows exported from notebook as JSON: ExportedExcel/<ModelName>.json
    [HttpGet("revised-comparison/{*fileName}")]
    public async Task<IActionResult> GetRevisedComparisonData(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("Missing file name.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid revised IFC file name.");
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var wholeModelJsonPath = Path.Combine(_env.ContentRootPath, "JSON Whole Model", $"{baseName}.json");
        var editedJsonPath = Path.Combine(_env.ContentRootPath, "JSON_Edit", $"{baseName}.json");

        if (!System.IO.File.Exists(wholeModelJsonPath) || !System.IO.File.Exists(editedJsonPath))
        {
            return NotFound("No Comparison Data Currently");
        }

        try
        {
            var wholeJson = await System.IO.File.ReadAllTextAsync(wholeModelJsonPath);
            var editedJson = await System.IO.File.ReadAllTextAsync(editedJsonPath);

            using var wholeDoc = JsonDocument.Parse(wholeJson);
            using var editedDoc = JsonDocument.Parse(editedJson);

            var rows = BuildRevisedComparisonRows(wholeDoc.RootElement, editedDoc.RootElement);
            return Ok(rows);
        }
        catch
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to build comparison data.");
        }
    }

    // GET api/models/revised-cobie-implementation/{fileName}
    // Returns grouped COBie implementation rows aligned with the Duplex notebook logic.
    [HttpGet("revised-cobie-implementation/{*fileName}")]
    public async Task<IActionResult> GetRevisedCobieImplementationData(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("Missing file name.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid revised IFC file name.");
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var beforeJsonPath = Path.Combine(_env.ContentRootPath, "JSON Whole Model", $"{baseName}.json");
        var afterJsonPath = Path.Combine(_env.ContentRootPath, "JSON_Edit", $"{baseName}.json");

        if (!System.IO.File.Exists(beforeJsonPath) || !System.IO.File.Exists(afterJsonPath))
        {
            return NotFound("No Comparison Data Currently");
        }

        try
        {
            var beforeJson = await System.IO.File.ReadAllTextAsync(beforeJsonPath);
            var afterJson = await System.IO.File.ReadAllTextAsync(afterJsonPath);

            using var beforeDoc = JsonDocument.Parse(beforeJson);
            using var afterDoc = JsonDocument.Parse(afterJson);

            var rows = BuildCobieImplementationRows(beforeDoc.RootElement, afterDoc.RootElement, out var totalChangedModelElements);
            Response.Headers["X-Total-Changed-Model-Elements"] = totalChangedModelElements.ToString();
            return Ok(rows);
        }
        catch
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to build COBie implementation data.");
        }
    }

    // GET api/models/revised-comparison-chart/{fileName}
    // Serves a pre-generated PNG nested pie chart from GeneratedCharts using common naming patterns.
    [HttpGet("revised-comparison-chart/{*fileName}")]
    public IActionResult GetRevisedComparisonChart(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("Missing file name.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid revised IFC file name.");
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var chartsFolder = Path.Combine(_env.ContentRootPath, "GeneratedCharts");
        var candidateNames = new[]
        {
            $"{baseName}_cobie_nested_pie.png",
            $"{baseName}-cobie-nested-pie.png",
            $"{baseName}.png"
        };

        var chartPath = candidateNames
            .Select(name => Path.Combine(chartsFolder, name))
            .FirstOrDefault(System.IO.File.Exists);

        if (string.IsNullOrWhiteSpace(chartPath))
        {
            return NotFound("No pre-generated chart found.");
        }

        var fileBytes = System.IO.File.ReadAllBytes(chartPath);
        return File(fileBytes, "image/png");
    }

    // GET api/models/revised-hierarchical-bubble-chart/{fileName}
    // Serves a pre-generated PNG hierarchical bubble chart from GeneratedCharts using common naming patterns.
    [HttpGet("revised-hierarchical-bubble-chart/{*fileName}")]
    public IActionResult GetRevisedHierarchicalBubbleChart(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("Missing file name.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid revised IFC file name.");
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var chartsFolder = Path.Combine(_env.ContentRootPath, "GeneratedCharts");
        var candidateNames = new[]
        {
            $"{baseName}_ifc_type_cobie_bubble.png",
            $"{baseName}-ifc-type-cobie-bubble.png",
            $"{baseName}_hierarchical_bubble.png",
            $"{baseName}-hierarchical-bubble.png",
            $"{baseName}_bubble.png",
            $"{baseName}-bubble.png"
        };

        var chartPath = candidateNames
            .Select(name => Path.Combine(chartsFolder, name))
            .FirstOrDefault(System.IO.File.Exists);

        if (string.IsNullOrWhiteSpace(chartPath))
        {
            return NotFound("No pre-generated hierarchical bubble chart found.");
        }

        var fileBytes = System.IO.File.ReadAllBytes(chartPath);
        return File(fileBytes, "image/png");
    }

    // GET api/models/revised-flip-summary/{fileName}
    // Returns grouped FLIP summary counts from JSON_Edit for models with FLIP naming flows.
    [HttpGet("revised-flip-summary/{*fileName}")]
    public async Task<IActionResult> GetRevisedFlipSummary(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("Missing file name.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid revised IFC file name.");
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var editedJsonPath = Path.Combine(_env.ContentRootPath, "JSON_Edit", $"{baseName}.json");
        if (!System.IO.File.Exists(editedJsonPath))
        {
            return NotFound("No FLIP summary data currently.");
        }

        try
        {
            var editedJson = await System.IO.File.ReadAllTextAsync(editedJsonPath);
            using var editedDoc = JsonDocument.Parse(editedJson);

            var groups = BuildRevisedFlipSummary(editedDoc.RootElement, baseName);
            if (groups.Count == 0)
            {
                return NotFound("No FLIP summary data currently.");
            }

            return Ok(new RevisedFlipSummaryResponse(
                TotalCount: groups.Sum(group => group.Count),
                Groups: groups));
        }
        catch
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to build FLIP summary data.");
        }
    }

    // GET api/models/revised-flip-chart/{fileName}
    // Serves a pre-generated PNG FLIP chart from GeneratedCharts using common naming patterns.
    [HttpGet("revised-flip-chart/{*fileName}")]
    public IActionResult GetRevisedFlipChart(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("Missing file name.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid revised IFC file name.");
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var chartsFolder = Path.Combine(_env.ContentRootPath, "GeneratedCharts");
        var candidateNames = new[]
        {
            $"{baseName}_flip_standard_donut.png",
            $"{baseName}-flip-standard-donut.png",
            $"{baseName}_flip_donut.png",
            $"{baseName}-flip-donut.png"
        };

        var chartPath = candidateNames
            .Select(name => Path.Combine(chartsFolder, name))
            .FirstOrDefault(System.IO.File.Exists);

        if (string.IsNullOrWhiteSpace(chartPath))
        {
            return NotFound("No pre-generated FLIP chart found.");
        }

        var fileBytes = System.IO.File.ReadAllBytes(chartPath);
        return File(fileBytes, "image/png");
    }

    private bool TryReadSqlDatabaseCobieNotebookOutputs(
        out string htmlTable,
        out string summaryText,
        out byte[] chartBytes,
        out string errorMessage)
    {
        htmlTable = null;
        summaryText = null;
        chartBytes = null;
        errorMessage = null;

        var notebookPath = Path.Combine(_env.ContentRootPath, "PyQueryDB", "IFCAllData-Query-COBie.ipynb");
        if (!System.IO.File.Exists(notebookPath))
        {
            errorMessage = "Notebook file not found.";
            return false;
        }

        try
        {
            var notebookRoot = JsonNode.Parse(System.IO.File.ReadAllText(notebookPath));
            var cells = notebookRoot?["cells"]?.AsArray();
            var chartCell = cells?
                .Reverse()
                .FirstOrDefault(cell => string.Equals(cell?["cell_type"]?.GetValue<string>(), "code", StringComparison.Ordinal));
            var outputs = chartCell?["outputs"]?.AsArray();

            if (outputs is null || outputs.Count == 0)
            {
                errorMessage = "Notebook output is not available.";
                return false;
            }

            foreach (var output in outputs)
            {
                var data = output?["data"];
                if (data is not null)
                {
                    if (htmlTable is null)
                    {
                        var htmlNode = data["text/html"];
                        var htmlValue = ReadNotebookText(htmlNode);
                        if (!string.IsNullOrWhiteSpace(htmlValue))
                        {
                            htmlTable = htmlValue;
                        }
                    }

                    if (chartBytes is null)
                    {
                        var imageNode = data["image/png"];
                        var base64Image = ReadNotebookText(imageNode);
                        if (!string.IsNullOrWhiteSpace(base64Image))
                        {
                            chartBytes = Convert.FromBase64String(base64Image);
                        }
                    }
                }

                if (summaryText is null)
                {
                    var textNode = output?["text"];
                    var outputText = ReadNotebookText(textNode);
                    if (!string.IsNullOrWhiteSpace(outputText))
                    {
                        summaryText = outputText.Trim();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(htmlTable) || chartBytes is null)
            {
                errorMessage = "Notebook chart output is incomplete.";
                return false;
            }

            return true;
        }
        catch
        {
            errorMessage = "Failed to read notebook output.";
            return false;
        }
    }

    private bool TryReadSqlDatabaseCobieRows(out List<SqlDatabaseCobieRow> rows, out string errorMessage)
    {
        rows = new List<SqlDatabaseCobieRow>();
        errorMessage = null;

        var dbPath = Path.Combine(_env.ContentRootPath, "SQL", "IFCAllData.db");
        if (!System.IO.File.Exists(dbPath))
        {
            errorMessage = "IFCAllData.db was not found.";
            return false;
        }

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    TRIM(NAME) AS NAME,
                    COUNT(*) AS ROW_COUNT,
                    SUM(CASE WHEN TRIM(COALESCE(COBie, '')) <> '' THEN 1 ELSE 0 END) AS COBIE_COUNT
                FROM ""IFCAllData-COBie""
                WHERE TRIM(COALESCE(NAME, '')) <> ''
                GROUP BY TRIM(NAME)
                ORDER BY ROW_COUNT DESC, TRIM(NAME)";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new SqlDatabaseCobieRow(
                    Name: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    RowCount: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    CobieCount: reader.IsDBNull(2) ? 0 : reader.GetInt32(2)));
            }

            return true;
        }
        catch
        {
            errorMessage = "Failed to query IFCAllData.db.";
            return false;
        }
    }

    private static string ReadNotebookText(JsonNode node)
    {
        return node switch
        {
            null => null,
            JsonArray array => string.Concat(array.Select(item => item?.GetValue<string>() ?? string.Empty)),
            _ => node.GetValue<string>()
        };
    }

    private static List<CobieImplementationRow> BuildCobieImplementationRows(JsonElement beforeRoot, JsonElement afterRoot, out int totalChangedModelElements)
    {
        var beforeRows = ExtractCobieRows(beforeRoot);
        var afterRows = ExtractCobieRows(afterRoot);

        var beforeByKey = beforeRows
            .GroupBy(x => x.ElementKey ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var afterByKey = afterRows
            .GroupBy(x => x.ElementKey ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var beforePrepared = beforeRows
            .Select(row => new
            {
                IfcType = NormalizeIfcType(row.IfcType),
                ItemType = InferItemType(row.CobieValue, row.IfcType)
            })
            .ToList();
        var afterPrepared = afterRows
            .Select(row => new
            {
                IfcType = NormalizeIfcType(row.IfcType),
                ItemType = InferItemType(row.CobieValue, row.IfcType)
            })
            .ToList();

        var itemTypeOrder = BuildOrderMap(beforePrepared.Select(x => x.ItemType).Concat(afterPrepared.Select(x => x.ItemType)));
        var ifcTypeOrder = BuildOrderMap(beforePrepared.Select(x => x.IfcType).Concat(afterPrepared.Select(x => x.IfcType)));

        var totalItemsByIfc = beforePrepared
            .GroupBy(x => x.IfcType ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var kvp in afterPrepared.GroupBy(x => x.IfcType ?? string.Empty, StringComparer.Ordinal))
        {
            var count = kvp.Count();
            if (!totalItemsByIfc.TryGetValue(kvp.Key, out var beforeCount) || count > beforeCount)
            {
                totalItemsByIfc[kvp.Key] = count;
            }
        }

        var totalItemsByItemType = beforePrepared
            .GroupBy(x => x.ItemType ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var kvp in afterPrepared.GroupBy(x => x.ItemType ?? string.Empty, StringComparer.Ordinal))
        {
            var count = kvp.Count();
            if (!totalItemsByItemType.TryGetValue(kvp.Key, out var beforeCount) || count > beforeCount)
            {
                totalItemsByItemType[kvp.Key] = count;
            }
        }

        var changedRows = new List<(string ItemType, string IfcType, string Name, string CobieValue, int SortItem, int SortIfc)>();

        foreach (var key in beforeByKey.Keys.Union(afterByKey.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            var hasBefore = beforeByKey.TryGetValue(key, out var beforeRow);
            var hasAfter = afterByKey.TryGetValue(key, out var afterRow);

            var beforeCobie = hasBefore ? beforeRow.CobieValue : string.Empty;
            var afterCobie = hasAfter ? afterRow.CobieValue : string.Empty;
            if (string.Equals(beforeCobie, afterCobie, StringComparison.Ordinal))
            {
                continue;
            }

            var rawIfcType = hasAfter && !string.IsNullOrWhiteSpace(afterRow.IfcType)
                ? afterRow.IfcType
                : (hasBefore ? beforeRow.IfcType : string.Empty);
            var ifcType = NormalizeIfcType(rawIfcType);

            var cobieValue = !string.IsNullOrWhiteSpace(afterCobie) ? afterCobie : beforeCobie;
            var name = hasAfter && !string.IsNullOrWhiteSpace(afterRow.ModelElementName)
                ? afterRow.ModelElementName
                : (hasBefore ? beforeRow.ModelElementName : string.Empty);
            var itemType = InferItemType(cobieValue, ifcType);

            changedRows.Add((
                ItemType: itemType,
                IfcType: ifcType,
                Name: name,
                CobieValue: cobieValue ?? string.Empty,
                SortItem: itemTypeOrder.TryGetValue(itemType, out var itemOrder) ? itemOrder : 999,
                SortIfc: ifcTypeOrder.TryGetValue(ifcType, out var ifcOrder) ? ifcOrder : 999));
        }

        var sortedChangedRows = changedRows
            .OrderBy(x => x.SortItem)
            .ThenBy(x => x.SortIfc)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        totalChangedModelElements = sortedChangedRows.Count;

        const int maxRowsPerItemType = 3;
        var result = new List<CobieImplementationRow>();

        foreach (var group in sortedChangedRows.GroupBy(x => x.ItemType, StringComparer.Ordinal))
        {
            foreach (var row in group.Take(maxRowsPerItemType))
            {
                var ifcTotal = totalItemsByIfc.TryGetValue(row.IfcType ?? string.Empty, out var totalCount)
                    ? totalCount
                    : 0;

                result.Add(new CobieImplementationRow(
                    ItemType: row.ItemType,
                    IfcType: row.IfcType,
                    TotalItemsCount: ifcTotal.ToString(),
                    Name: row.Name,
                    CobieValue: row.CobieValue));
            }

            result.Add(new CobieImplementationRow("---", "---", "---", "---", "---"));

            var groupTotal = totalItemsByItemType.TryGetValue(group.Key ?? string.Empty, out var itemTotal)
                ? itemTotal
                : 0;
            result.Add(new CobieImplementationRow(string.Empty, string.Empty, $"Total Items: {groupTotal}", string.Empty, string.Empty));
            result.Add(new CobieImplementationRow(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
        }

        return result;
    }

    private static List<(string ElementKey, string IfcType, string ModelElementName, string CobieValue)> ExtractCobieRows(JsonElement root)
    {
        var result = new List<(string ElementKey, string IfcType, string ModelElementName, string CobieValue)>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = Normalize(TryGetString(item, "Name"));
            var externalId = Normalize(TryGetString(item, "ExternalId"));

            item.TryGetProperty("Properties", out var properties);
            var typeProp = FindProperty(properties, "Type", "Item") ?? FindProperty(properties, "Type", null);
            var nameProp = FindProperty(properties, "Name", "Item") ?? FindProperty(properties, "Name", null);
            var cobieProp = FindProperty(properties, "COBie", null);

            var modelElementName = !string.IsNullOrWhiteSpace(name)
                ? name
                : (nameProp?.Value ?? string.Empty);

            var elementKey = !string.IsNullOrWhiteSpace(externalId)
                ? externalId
                : modelElementName;

            result.Add((
                ElementKey: elementKey ?? string.Empty,
                IfcType: typeProp?.Value ?? string.Empty,
                ModelElementName: modelElementName ?? string.Empty,
                CobieValue: cobieProp?.Value ?? string.Empty));
        }

        return result;
    }

    private static (string Category, string DisplayName, string Value)? FindProperty(JsonElement properties, string displayName, string category)
    {
        if (properties.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var prop in properties.EnumerateArray())
        {
            if (prop.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var propDisplayName = Normalize(TryGetString(prop, "displayName"));
            if (!string.Equals(propDisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var propCategory = Normalize(TryGetString(prop, "category"));
            if (!string.IsNullOrWhiteSpace(category) && !string.Equals(propCategory, category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return (
                Category: propCategory,
                DisplayName: propDisplayName,
                Value: Normalize(TryGetString(prop, "value")));
        }

        return null;
    }

    private static string NormalizeIfcType(string rawIfcType)
    {
        return Normalize(rawIfcType).ToUpperInvariant();
    }

    private static string ExtractCobieItemType(string cobieValue)
    {
        var value = Normalize(cobieValue);
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var separatorIndex = value.IndexOf(':');
        if (separatorIndex < 0)
        {
            return string.Empty;
        }

        if (separatorIndex + 1 >= value.Length)
        {
            return string.Empty;
        }

        return Normalize(value.Substring(separatorIndex + 1));
    }

    private static string InferItemType(string cobieValue, string ifcType)
    {
        var extracted = ExtractCobieItemType(cobieValue);
        if (!string.IsNullOrWhiteSpace(extracted))
        {
            return extracted;
        }

        var normalizedIfc = NormalizeIfcType(ifcType);
        return string.IsNullOrWhiteSpace(normalizedIfc) ? "Unclassified" : normalizedIfc;
    }

    private static List<RevisedFlipSummaryGroup> BuildRevisedFlipSummary(JsonElement root, string baseName)
    {
        var flipTypeDefinitions = GetFlipSummaryTypeDefinitions(baseName);
        if (flipTypeDefinitions.Count == 0 || root.ValueKind != JsonValueKind.Array)
        {
            return new List<RevisedFlipSummaryGroup>();
        }

        var orderMap = flipTypeDefinitions
            .Select((entry, index) => new { entry.Key, Index = index })
            .ToDictionary(x => x.Key, x => x.Index, StringComparer.Ordinal);
        var labelMap = flipTypeDefinitions
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        var matchedRows = new List<(string IfcType, string FlipName)>();

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            item.TryGetProperty("Properties", out var properties);
            var typeProp = FindProperty(properties, "Type", "Item") ?? FindProperty(properties, "Type", null);
            var ifcType = NormalizeIfcType(typeProp?.Value ?? string.Empty);
            if (!labelMap.ContainsKey(ifcType))
            {
                continue;
            }

            var flipName = Normalize(FindProperty(properties, "Name", "IFC")?.Value);
            if (string.IsNullOrWhiteSpace(flipName))
            {
                continue;
            }

            matchedRows.Add((IfcType: ifcType, FlipName: flipName));
        }

        return matchedRows
            .GroupBy(row => row.IfcType, StringComparer.Ordinal)
            .OrderBy(group => orderMap.TryGetValue(group.Key, out var order) ? order : int.MaxValue)
            .Select(group => new RevisedFlipSummaryGroup(
                Label: labelMap[group.Key],
                IfcType: group.Key,
                Count: group.Count(),
                Names: group
                    .GroupBy(row => row.FlipName, StringComparer.Ordinal)
                    .Select(nameGroup => new RevisedFlipNameCount(nameGroup.Key, nameGroup.Count()))
                    .OrderByDescending(nameGroup => nameGroup.Count)
                    .ThenBy(nameGroup => nameGroup.Name, StringComparer.Ordinal)
                    .ToArray()))
            .ToList();
    }

    private static IReadOnlyList<KeyValuePair<string, string>> GetFlipSummaryTypeDefinitions(string baseName)
    {
        var normalizedBaseName = Normalize(baseName).ToLowerInvariant();
        if (string.Equals(normalizedBaseName, "ifc4_samplehouse", StringComparison.Ordinal))
        {
            return new[]
            {
                new KeyValuePair<string, string>("IFCDOOR", "Door"),
                new KeyValuePair<string, string>("IFCWINDOW", "Window")
            };
        }

        if (string.Equals(normalizedBaseName, "ifc2x3_duplex_architecture", StringComparison.Ordinal))
        {
            return new[]
            {
                new KeyValuePair<string, string>("IFCDOOR", "Door"),
                new KeyValuePair<string, string>("IFCWINDOW", "Window")
            };
        }

        if (string.Equals(normalizedBaseName, "snowdon+towers+sample+structural2x3", StringComparison.Ordinal))
        {
            return new[]
            {
                new KeyValuePair<string, string>("IFCCOLUMN", "Column"),
                new KeyValuePair<string, string>("IFCBEAM", "Beam"),
                new KeyValuePair<string, string>("IFCSLAB", "Slab")
            };
        }

        return Array.Empty<KeyValuePair<string, string>>();
    }

    private static Dictionary<string, int> BuildOrderMap(IEnumerable<string> values)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in values)
        {
            var value = Normalize(raw);
            if (string.IsNullOrWhiteSpace(value) || order.ContainsKey(value))
            {
                continue;
            }

            order[value] = order.Count + 1;
        }

        return order;
    }

    private static List<RevisedComparisonRow> BuildRevisedComparisonRows(JsonElement wholeRoot, JsonElement editedRoot)
    {
        var wholeMap = BuildComparisonMap(wholeRoot);
        var editedMap = BuildComparisonMap(editedRoot);

        var rawRows = new List<(string ExistingName, string EditedName, string Category, string DisplayName)>();

        foreach (var key in wholeMap.Keys.Intersect(editedMap.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var whole = wholeMap[key];
            var edited = editedMap[key];

            var wholeName = Normalize(whole.Name);
            var editedName = Normalize(edited.Name);

            if (string.Equals(wholeName, editedName, StringComparison.Ordinal))
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(wholeName) && string.IsNullOrWhiteSpace(editedName))
            {
                continue;
            }

            var wholeNameProp = FindNameProperty(whole.Properties);
            var editedNameProp = FindNameProperty(edited.Properties);

            var category = !string.IsNullOrWhiteSpace(wholeNameProp.Category)
                ? wholeNameProp.Category
                : editedNameProp.Category;
            var displayName = !string.IsNullOrWhiteSpace(wholeNameProp.DisplayName)
                ? wholeNameProp.DisplayName
                : editedNameProp.DisplayName;

            rawRows.Add((wholeName, editedName, category, displayName));
        }

        var grouped = rawRows
            .GroupBy(r => r.EditedName ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var countsByExistingName = g
                    .GroupBy(x => x.ExistingName ?? string.Empty, StringComparer.Ordinal)
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => $"{x.Key} (x{x.Count()})");

                var first = g.First();
                return new RevisedComparisonRow(
                    ExistingModelName: string.Join("\n", countsByExistingName),
                    EditedName: g.Key,
                    PropertyCategory: first.Category ?? string.Empty,
                    PropertyDisplayName: first.DisplayName ?? string.Empty,
                    Count: g.Count());
            })
            .ToList();

        return grouped;
    }

    private static Dictionary<string, (string Name, JsonElement Properties)> BuildComparisonMap(JsonElement root)
    {
        var result = new Dictionary<string, (string Name, JsonElement Properties)>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = BuildMatchKey(item);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var name = TryGetString(item, "Name");
            var hasProps = item.TryGetProperty("Properties", out var props);
            result[key] = (name, hasProps ? props : default);
        }

        return result;
    }

    private static string BuildMatchKey(JsonElement item)
    {
        int? dbId = null;
        if (item.TryGetProperty("DbId", out var dbIdEl))
        {
            if (dbIdEl.ValueKind == JsonValueKind.Number && dbIdEl.TryGetInt32(out var parsedDbId))
            {
                dbId = parsedDbId;
            }
            else if (dbIdEl.ValueKind == JsonValueKind.String && int.TryParse(dbIdEl.GetString(), out var parsedDbIdFromString))
            {
                dbId = parsedDbIdFromString;
            }
        }

        var externalId = Normalize(TryGetString(item, "ExternalId"));

        if (dbId.HasValue && !string.IsNullOrWhiteSpace(externalId))
        {
            return $"DbId:{dbId.Value}|ExternalId:{externalId}";
        }
        if (dbId.HasValue)
        {
            return $"DbId:{dbId.Value}";
        }
        if (!string.IsNullOrWhiteSpace(externalId))
        {
            return $"ExternalId:{externalId}";
        }

        return null;
    }

    private static (string Category, string DisplayName, string Value) FindNameProperty(JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Array)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        foreach (var prop in properties.EnumerateArray())
        {
            if (prop.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var displayName = Normalize(TryGetString(prop, "displayName"));
            if (!string.Equals(displayName, "name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return (
                Category: Normalize(TryGetString(prop, "category")),
                DisplayName: displayName,
                Value: Normalize(TryGetString(prop, "value")));
        }

        return (string.Empty, string.Empty, string.Empty);
    }

    private static string TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    // GET api/models/revised/download/{fileName}
    // Downloads a revised IFC file from Revised_IFC.
    [HttpGet("revised/download/{*fileName}")]
    public IActionResult DownloadRevisedModel(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("Missing file name.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid revised IFC file name.");
        }

        var revisedFolder = Path.Combine(_env.ContentRootPath, "Revised_IFC");
        var filePath = Path.Combine(revisedFolder, safeFileName);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound($"Revised IFC file not found: {safeFileName}");
        }

        var fileBytes = System.IO.File.ReadAllBytes(filePath);
        return File(fileBytes, "application/octet-stream", safeFileName);
    }

    // DELETE api/models/revised/{fileName}
    // Deletes a revised IFC file from Revised_IFC.
    [HttpDelete("revised/{*fileName}")]
    public IActionResult DeleteRevisedModel(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("Missing file name.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid revised IFC file name.");
        }

        var revisedFolder = Path.Combine(_env.ContentRootPath, "Revised_IFC");
        var filePath = Path.Combine(revisedFolder, safeFileName);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound($"Revised IFC file not found: {safeFileName}");
        }

        System.IO.File.Delete(filePath);
        return NoContent();
    }

    // POST api/models/extract-data
    // Minimal backend endpoint for receiving extracted metadata from the client.
    [HttpPost("extract-data")]
    public ActionResult<ExtractDataRequest> ExtractData([FromBody] ExtractDataRequest request)
    {
        if (request == null)
        {
            return BadRequest("Missing request body.");
        }
        return Ok(request);
    }

    // 4.4.3.2 POST api/models/export-element-properties
    // Uses APS Model Derivative to fetch the selected element's properties on the server,
    // flattens them into an array, and writes them to JSON Export/<ElementName>.json.
    [HttpPost("export-element-properties")]
    public async Task<ActionResult<ExportElementPropertiesResponse>> ExportElementProperties([FromBody] ExportElementPropertiesRequest request)
    {
        if (request == null)
        {
            return BadRequest("Missing request body.");
        }
        if (string.IsNullOrWhiteSpace(request.Urn))
        {
            return BadRequest("Missing 'urn'.");
        }
        if (request.DbId <= 0)
        {
            return BadRequest("Missing or invalid 'dbId'.");
        }

        var viewGuid = string.IsNullOrWhiteSpace(request.ViewGuid)
            ? await _aps.GetDefaultModelViewGuid(request.Urn)
            : request.ViewGuid;

        using var propsDoc = await _aps.GetElementProperties(request.Urn, viewGuid, request.DbId);
        var flattened = FlattenPropertiesToArray(propsDoc.RootElement, out var elementName);
        if (string.IsNullOrWhiteSpace(elementName))
        {
            elementName = $"dbid_{request.DbId}";
        }

        var safeFileName = MakeSafeFileName(elementName) + ".json";
        var exportFolder = Path.Combine(_env.ContentRootPath, "JSON Export");
        Directory.CreateDirectory(exportFolder);

        var outputPath = Path.Combine(exportFolder, safeFileName);
        var json = flattened.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(outputPath, json);

        return Ok(new ExportElementPropertiesResponse(safeFileName, "JSON Export", flattened.Count, elementName));
    }

    // POST api/models/export-whole-model-properties
    // Fetches the entire model properties payload from APS Model Derivative,
    // converts it into an array of elements (sorted by Name), and writes it to JSON Whole Model/<ModelName>.json.
    [HttpPost("export-whole-model-properties")]
    public async Task<ActionResult<ExportWholeModelPropertiesResponse>> ExportWholeModelProperties([FromBody] ExportWholeModelPropertiesRequest request)
    {
        if (request == null)
        {
            return BadRequest("Missing request body.");
        }
        if (string.IsNullOrWhiteSpace(request.Urn))
        {
            return BadRequest("Missing 'urn'.");
        }

        var viewGuid = string.IsNullOrWhiteSpace(request.ViewGuid)
            ? await _aps.GetDefaultModelViewGuid(request.Urn)
            : request.ViewGuid;

        JsonArray elements;
        try
        {
            using var propsDoc = await _aps.GetWholeModelProperties(request.Urn, viewGuid);
            elements = ConvertWholeModelToElementArray(propsDoc.RootElement);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.RequestEntityTooLarge)
        {
            elements = await BuildWholeModelElementArrayFallback(request.Urn, viewGuid);
        }

        var sortedNodes = elements
            .Select(n => (JsonObject)n)
            .OrderBy(o => GetJsonObjectString(o, "Name"), StringComparer.OrdinalIgnoreCase)
            .Select(o => o.DeepClone())
            .ToArray();
        elements = new JsonArray(sortedNodes);

        var modelName = GetModelNameFromUrn(request.Urn);
        if (string.IsNullOrWhiteSpace(modelName))
        {
            modelName = request.Urn;
        }
        modelName = Path.GetFileNameWithoutExtension(modelName);
        var safeFileName = MakeSafeFileName(modelName) + ".json";

        var exportFolder = Path.Combine(_env.ContentRootPath, "JSON Whole Model");
        Directory.CreateDirectory(exportFolder);

        var outputPath = Path.Combine(exportFolder, safeFileName);
        var json = elements.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(outputPath, json);

        return Ok(new ExportWholeModelPropertiesResponse(safeFileName, "JSON Whole Model", elements.Count, modelName));
    }

    private async Task<JsonArray> BuildWholeModelElementArrayFallback(string urn, string viewGuid)
    {
        using var hierarchyDoc = await _aps.GetModelHierarchy(urn, viewGuid);
        var objectIds = ExtractHierarchyObjectIds(hierarchyDoc.RootElement);
        if (objectIds.Count == 0)
        {
            return new JsonArray();
        }

        var semaphore = new SemaphoreSlim(WholeModelFallbackConcurrency);
        var tasks = objectIds.Select(async dbId =>
        {
            await semaphore.WaitAsync();
            try
            {
                using var propsDoc = await _aps.GetElementProperties(urn, viewGuid, dbId);
                return ConvertElementPropertiesToElementObject(propsDoc.RootElement, dbId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Skipping dbId {dbId} during fallback whole-model export: {ex.Message}");
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var nodes = await Task.WhenAll(tasks);
        return new JsonArray(nodes.Where(node => node != null).Select(node => node!).ToArray());
    }

    private static string GetJsonObjectString(JsonObject obj, string propertyName)
    {
        if (obj == null)
        {
            return string.Empty;
        }
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node == null)
        {
            return string.Empty;
        }
        if (node is JsonValue value && value.TryGetValue<string>(out var s) && s != null)
        {
            return s;
        }
        return string.Empty;
    }

    private static JsonArray FlattenPropertiesToArray(JsonElement root, out string elementName)
    {
        // Expected shape (typical for objectid query): { data: { collection: [ { name: "...", properties: { Category: { Prop: Value } } } ] } }
        elementName = null;
        var array = new JsonArray();

        if (!root.TryGetProperty("data", out var dataEl))
        {
            return array;
        }

        if (!dataEl.TryGetProperty("collection", out var collectionEl) || collectionEl.ValueKind != JsonValueKind.Array || collectionEl.GetArrayLength() == 0)
        {
            return array;
        }

        var itemEl = collectionEl[0];
        if (itemEl.TryGetProperty("name", out var nameEl))
        {
            elementName = nameEl.GetString();
        }

        if (!itemEl.TryGetProperty("properties", out var propertiesEl) || propertiesEl.ValueKind != JsonValueKind.Object)
        {
            return array;
        }

        foreach (var category in propertiesEl.EnumerateObject())
        {
            if (category.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            foreach (var prop in category.Value.EnumerateObject())
            {
                array.Add(new JsonObject
                {
                    ["category"] = category.Name,
                    ["displayName"] = prop.Name,
                    ["value"] = JsonNode.Parse(prop.Value.GetRawText())
                });
            }
        }

        return array;
    }

    private static JsonArray ConvertWholeModelToElementArray(JsonElement root)
    {
        // Expected shape (no objectid query): { data: { collection: [ { objectid, name, externalId?, properties: { Category: { Prop: Value } } } ... ] } }
        var result = new JsonArray();

        if (!root.TryGetProperty("data", out var dataEl))
        {
            return result;
        }

        if (!dataEl.TryGetProperty("collection", out var collectionEl) || collectionEl.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in collectionEl.EnumerateArray())
        {
            var elementObj = new JsonObject();

            if (item.TryGetProperty("name", out var nameEl))
            {
                elementObj["Name"] = nameEl.GetString();
            }

            if (item.TryGetProperty("objectid", out var objectIdEl) && objectIdEl.ValueKind == JsonValueKind.Number && objectIdEl.TryGetInt32(out var dbId))
            {
                elementObj["DbId"] = dbId;
            }

            if (item.TryGetProperty("externalId", out var externalIdEl) && externalIdEl.ValueKind == JsonValueKind.String)
            {
                elementObj["ExternalId"] = externalIdEl.GetString();
            }

            var propsArray = new JsonArray();
            if (item.TryGetProperty("properties", out var propertiesEl) && propertiesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var category in propertiesEl.EnumerateObject())
                {
                    if (category.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    foreach (var prop in category.Value.EnumerateObject())
                    {
                        propsArray.Add(new JsonObject
                        {
                            ["category"] = category.Name,
                            ["displayName"] = prop.Name,
                            ["value"] = JsonNode.Parse(prop.Value.GetRawText())
                        });
                    }
                }
            }

            elementObj["Properties"] = propsArray;
            result.Add(elementObj);
        }

        return result;
    }

    private static JsonObject ConvertElementPropertiesToElementObject(JsonElement root, int fallbackDbId)
    {
        var elementObj = new JsonObject
        {
            ["DbId"] = fallbackDbId,
            ["Properties"] = new JsonArray()
        };

        if (!root.TryGetProperty("data", out var dataEl))
        {
            return elementObj;
        }

        if (!dataEl.TryGetProperty("collection", out var collectionEl) || collectionEl.ValueKind != JsonValueKind.Array || collectionEl.GetArrayLength() == 0)
        {
            return elementObj;
        }

        var itemEl = collectionEl[0];

        if (itemEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
        {
            elementObj["Name"] = nameEl.GetString();
        }

        if (itemEl.TryGetProperty("objectid", out var objectIdEl) && objectIdEl.ValueKind == JsonValueKind.Number && objectIdEl.TryGetInt32(out var objectId))
        {
            elementObj["DbId"] = objectId;
        }

        if (itemEl.TryGetProperty("externalId", out var externalIdEl) && externalIdEl.ValueKind == JsonValueKind.String)
        {
            elementObj["ExternalId"] = externalIdEl.GetString();
        }

        var propsArray = new JsonArray();
        if (itemEl.TryGetProperty("properties", out var propertiesEl) && propertiesEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var category in propertiesEl.EnumerateObject())
            {
                if (category.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var prop in category.Value.EnumerateObject())
                {
                    propsArray.Add(new JsonObject
                    {
                        ["category"] = category.Name,
                        ["displayName"] = prop.Name,
                        ["value"] = JsonNode.Parse(prop.Value.GetRawText())
                    });
                }
            }
        }

        elementObj["Properties"] = propsArray;
        return elementObj;
    }

    private static List<int> ExtractHierarchyObjectIds(JsonElement root)
    {
        var ids = new HashSet<int>();

        if (!root.TryGetProperty("data", out var dataEl))
        {
            return ids.OrderBy(id => id).ToList();
        }

        if (dataEl.TryGetProperty("objects", out var objectsEl) && objectsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var obj in objectsEl.EnumerateArray())
            {
                CollectHierarchyObjectIds(obj, ids);
            }
        }

        return ids.OrderBy(id => id).ToList();
    }

    private static void CollectHierarchyObjectIds(JsonElement node, HashSet<int> ids)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (node.TryGetProperty("objectid", out var objectIdEl) && objectIdEl.ValueKind == JsonValueKind.Number && objectIdEl.TryGetInt32(out var objectId) && objectId > 0)
        {
            ids.Add(objectId);
        }

        if (!node.TryGetProperty("objects", out var childrenEl) || childrenEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in childrenEl.EnumerateArray())
        {
            CollectHierarchyObjectIds(child, ids);
        }
    }

    private static string GetModelNameFromUrn(string urn)
    {
        // URN is the base64 (unpadded) encoding of the OSS objectId, which ends with "/<objectKey>".
        // Example objectId: "urn:adsk.objects:os.object:bucketKey/Ifc2x3_Duplex_Mechanical.ifc"
        try
        {
            if (string.IsNullOrWhiteSpace(urn))
            {
                return null;
            }

            var padded = urn.Trim();
            var mod = padded.Length % 4;
            if (mod != 0)
            {
                padded = padded + new string('=', 4 - mod);
            }

            var bytes = Convert.FromBase64String(padded);
            var objectId = Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return null;
            }

            var slash = objectId.LastIndexOf('/');
            if (slash < 0 || slash == objectId.Length - 1)
            {
                return objectId;
            }

            return objectId[(slash + 1)..];
        }
        catch
        {
            return null;
        }
    }

    private static string MakeSafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "export";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]))
            {
                chars[i] = '_';
            }
        }
        var cleaned = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
    }

    [HttpGet("{urn}/status")]
    public async Task<TranslationStatus> GetModelStatus(string urn)
    {
        var status = await _aps.GetTranslationStatus(urn);
        return status;
    }

    public class UploadModelForm
    {
        [FromForm(Name = "model-zip-entrypoint")]
        public string Entrypoint { get; set; }

        [FromForm(Name = "model-file")]
        public IFormFile File { get; set; }
    }

    [HttpPost]
    [RequestSizeLimit(600L * 1024L * 1024L)]
    [RequestFormLimits(MultipartBodyLengthLimit = 600L * 1024L * 1024L)]
    public async Task<BucketObject> UploadAndTranslateModel([FromForm] UploadModelForm form)
    {
        using var stream = form.File.OpenReadStream();
        var obj = await _aps.UploadModel(form.File.FileName, stream);
        var job = await _aps.TranslateModel(obj.ObjectId, form.Entrypoint);
        return new BucketObject(obj.ObjectKey, job.Urn);
    }

    // DELETE api/models/{objectKey}
    [HttpDelete("{objectKey}")]
    public async Task<IActionResult> Delete(string objectKey)
    {
        await _aps.DeleteObject(objectKey);
        return NoContent();
    }

    // GET api/models/download/{objectKey}
    // Redirects to a short-lived signed download URL.
    [HttpGet("download/{*objectKey}")]
    public async Task<IActionResult> Download(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return BadRequest("Missing object key.");
        }

        try
        {
            var signedUrl = await _aps.GetSignedDownloadUrl(objectKey);
            return Redirect(signedUrl);
        }
        catch (Exception ex)
        {
            return StatusCode((int)HttpStatusCode.BadGateway, ex.Message);
        }
    }

    public record TransformedDataPathResponse(string FullPath, string FileName);
    public record ApplyTransformedDataResponse(
        string SourceIfcPath,
        string JsonPath,
        string RevisedIfcPath,
        string FileName,
        int ElementCount,
        int PropertyCount,
        int ElementsUpdated,
        int PropertiesUpdated,
        string OutputIfc);

    // GET api/models/transformed-data?urn=<modelUrn>
    // Resolves the transformed JSON path in JSON_Edit corresponding to the selected model.
    [HttpGet("transformed-data")]
    public ActionResult<TransformedDataPathResponse> GetTransformedData([FromQuery] string urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return BadRequest("Missing 'urn'.");
        }

        var modelName = GetModelNameFromUrn(urn);
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return BadRequest("Could not resolve model name from 'urn'.");
        }

        var baseName = Path.GetFileNameWithoutExtension(modelName);
        var safeFileName = MakeSafeFileName(baseName) + ".json";
        var transformedPath = Path.Combine(_env.ContentRootPath, "JSON_Edit", safeFileName);

        if (!System.IO.File.Exists(transformedPath))
        {
            return NotFound($"Transformed data file not found in JSON_Edit: {safeFileName}");
        }

        return Ok(new TransformedDataPathResponse(transformedPath, safeFileName));
    }

    // POST api/models/apply-transformed-data?urn=<modelUrn>
    // Applies metadata from JSON_Edit/<ModelName>.json to the source IFC and saves Revised_IFC/<ModelName>.ifc.
    [HttpPost("apply-transformed-data")]
    public async Task<ActionResult<ApplyTransformedDataResponse>> ApplyTransformedData([FromQuery] string urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return BadRequest("Missing 'urn'.");
        }

        var modelName = GetModelNameFromUrn(urn);
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return BadRequest("Could not resolve model name from 'urn'.");
        }

        var objectKey = modelName;
        var baseName = Path.GetFileNameWithoutExtension(modelName);
        var jsonFileName = MakeSafeFileName(baseName) + ".json";
        var jsonPath = Path.Combine(_env.ContentRootPath, "JSON_Edit", jsonFileName);
        if (!System.IO.File.Exists(jsonPath))
        {
            return NotFound($"Transformed data file not found in JSON_Edit: {jsonFileName}");
        }

        var revisedFolder = Path.Combine(_env.ContentRootPath, "Revised_IFC");
        Directory.CreateDirectory(revisedFolder);

        var sourceIfcPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{Path.GetFileName(modelName)}");
        var revisedIfcPath = Path.Combine(revisedFolder, Path.GetFileName(modelName));

        try
        {
            var signedUrl = await _aps.GetSignedDownloadUrl(objectKey);
            using (var http = new HttpClient())
            using (var response = await http.GetAsync(signedUrl))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(sourceIfcPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
            }

            var scriptPath = Path.Combine(_env.ContentRootPath, "PyDataTransform", "ApplyTransformedJsonToIfc.py");
            if (!System.IO.File.Exists(scriptPath))
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, "Missing script: PyDataTransform/ApplyTransformedJsonToIfc.py");
            }

            var venvPython = Path.Combine(_env.ContentRootPath, ".venv", "Scripts", "python.exe");
            var pythonExecutable = System.IO.File.Exists(venvPython) ? venvPython : "python";

            var psi = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = $"\"{scriptPath}\" --input-ifc \"{sourceIfcPath}\" --input-json \"{jsonPath}\" --output-ifc \"{revisedIfcPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _env.ContentRootPath
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, "Failed to start Python process.");
            }

            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    $"Apply transformed data failed (exit {process.ExitCode}). stdout: {stdOut} stderr: {stdErr}");
            }

            var elementCount = 0;
            var propertyCount = 0;
            if (!string.IsNullOrWhiteSpace(stdOut))
            {
                var lines = stdOut
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();

                var jsonLine = lines.LastOrDefault(l => l.StartsWith("{") && l.EndsWith("}"));
                if (!string.IsNullOrWhiteSpace(jsonLine))
                {
                    using var doc = JsonDocument.Parse(jsonLine);
                    if (doc.RootElement.TryGetProperty("elementsUpdated", out var el) && el.ValueKind == JsonValueKind.Number)
                    {
                        elementCount = el.GetInt32();
                    }
                    if (doc.RootElement.TryGetProperty("propertiesUpdated", out var pl) && pl.ValueKind == JsonValueKind.Number)
                    {
                        propertyCount = pl.GetInt32();
                    }
                }
            }

            return Ok(new ApplyTransformedDataResponse(
                sourceIfcPath,
                jsonPath,
                revisedIfcPath,
                Path.GetFileName(modelName),
                elementCount,
                propertyCount,
                elementCount,
                propertyCount,
                revisedIfcPath));
        }
        catch (Exception ex)
        {
            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(sourceIfcPath))
                {
                    System.IO.File.Delete(sourceIfcPath);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
