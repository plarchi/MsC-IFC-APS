using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;

[ApiController]
[Route("api/[controller]")]
public class ModelsController : ControllerBase
{
    public record BucketObject(string name, string urn);

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

    private readonly APS _aps;
    private readonly IWebHostEnvironment _env;

    public ModelsController(APS aps, IWebHostEnvironment env)
    {
        _aps = aps;
        _env = env;
    }

    [HttpGet()]
    public async Task<IEnumerable<BucketObject>> GetModels()
    {
        var objects = await _aps.GetObjects();
        return from o in objects
               select new BucketObject(o.ObjectKey, APS.Base64Encode(o.ObjectId));
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

    // POST api/models/export-element-properties
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

    [HttpPost(), DisableRequestSizeLimit]
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
}
