using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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

    private readonly APS _aps;

    public ModelsController(APS aps)
    {
        _aps = aps;
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
