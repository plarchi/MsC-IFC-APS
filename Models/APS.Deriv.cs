using System.Collections.Generic;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.ModelDerivative;
using Autodesk.ModelDerivative.Model;

public record TranslationStatus(string Status, string Progress, IEnumerable<string> Messages);

public partial class APS
{
    private static readonly HttpClient _http = new HttpClient();

    public static string Base64Encode(string plainText)
    {
        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return System.Convert.ToBase64String(plainTextBytes).TrimEnd('=');
    }

    public async Task<Job> TranslateModel(string objectId, string rootFilename)
    {
        var auth = await GetInternalToken();
        var modelDerivativeClient = new ModelDerivativeClient();
        var payload = new JobPayload
        {
            Input = new JobPayloadInput
            {
                Urn = Base64Encode(objectId)
            },
            Output = new JobPayloadOutput
            {
                Formats =
                [
                    new JobPayloadFormatSVF2
                    {
                        Views = [View._2d, View._3d]
                    }
                ]
            }
        };
        if (!string.IsNullOrEmpty(rootFilename))
        {
            payload.Input.RootFilename = rootFilename;
            payload.Input.CompressedUrn = true;
        }
        var job = await modelDerivativeClient.StartJobAsync(jobPayload: payload, region: Region.US, accessToken: auth.AccessToken);
        return job;
    }

    public async Task<TranslationStatus> GetTranslationStatus(string urn)
    {
        var auth = await GetInternalToken();
        var modelDerivativeClient = new ModelDerivativeClient();
        try
        {
            var manifest = await modelDerivativeClient.GetManifestAsync(urn, accessToken: auth.AccessToken);
            return new TranslationStatus(manifest.Status, manifest.Progress, []);
        }
        catch (ModelDerivativeApiException ex)
        {
            if (ex.HttpResponseMessage.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new TranslationStatus("n/a", "", null);
            }
            else
            {
                throw;
            }
        }
    }

    public async Task<string> GetDefaultModelViewGuid(string urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            throw new ArgumentException("Missing URN.", nameof(urn));
        }

        var auth = await GetInternalToken();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var dataEl) || !dataEl.TryGetProperty("metadata", out var metadataEl) || metadataEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Unexpected metadata response.");
        }

        string firstGuid = null;
        foreach (var item in metadataEl.EnumerateArray())
        {
            if (!item.TryGetProperty("guid", out var guidEl))
            {
                continue;
            }
            var guid = guidEl.GetString();
            if (string.IsNullOrWhiteSpace(guid))
            {
                continue;
            }
            firstGuid ??= guid;
            if (item.TryGetProperty("role", out var roleEl) && string.Equals(roleEl.GetString(), "3d", StringComparison.OrdinalIgnoreCase))
            {
                return guid;
            }
        }

        if (string.IsNullOrWhiteSpace(firstGuid))
        {
            throw new InvalidOperationException("No metadata view GUID found.");
        }

        return firstGuid;
    }

    public async Task<JsonDocument> GetElementProperties(string urn, string viewGuid, int dbId)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            throw new ArgumentException("Missing URN.", nameof(urn));
        }
        if (string.IsNullOrWhiteSpace(viewGuid))
        {
            throw new ArgumentException("Missing view GUID.", nameof(viewGuid));
        }
        if (dbId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dbId), "dbId must be > 0.");
        }

        var auth = await GetInternalToken();
        var url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{viewGuid}/properties?objectid={dbId}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    public async Task<JsonDocument> GetModelHierarchy(string urn, string viewGuid)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            throw new ArgumentException("Missing URN.", nameof(urn));
        }
        if (string.IsNullOrWhiteSpace(viewGuid))
        {
            throw new ArgumentException("Missing view GUID.", nameof(viewGuid));
        }

        var auth = await GetInternalToken();
        var url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{viewGuid}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    public async Task<JsonDocument> GetWholeModelProperties(string urn, string viewGuid)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            throw new ArgumentException("Missing URN.", nameof(urn));
        }
        if (string.IsNullOrWhiteSpace(viewGuid))
        {
            throw new ArgumentException("Missing view GUID.", nameof(viewGuid));
        }

        var auth = await GetInternalToken();
        var url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{viewGuid}/properties";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }
}
