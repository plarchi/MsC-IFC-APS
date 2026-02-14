using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.Oss;
using Autodesk.Oss.Model;

public partial class APS
{
    private async Task EnsureBucketExists(string bucketKey)
    {
        var auth = await GetInternalToken();
        var ossClient = new OssClient();
        try
        {
            await ossClient.GetBucketDetailsAsync(bucketKey, accessToken: auth.AccessToken);
        }
        catch (OssApiException ex)
        {
            if (ex.HttpResponseMessage.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var payload = new CreateBucketsPayload
                {
                    BucketKey = bucketKey,
                    PolicyKey = PolicyKey.Persistent
                };
                await ossClient.CreateBucketAsync(Region.US, payload, auth.AccessToken);
            }
            else
            {
                throw;
            }
        }
    }

    public async Task<ObjectDetails> UploadModel(string objectName, Stream stream)
    {
        await EnsureBucketExists(_bucket);
        var auth = await GetInternalToken();
        var ossClient = new OssClient();
        var objectDetails = await ossClient.UploadObjectAsync(_bucket, objectName, stream, accessToken: auth.AccessToken);
        return objectDetails;
    }

    public async Task<IEnumerable<ObjectDetails>> GetObjects()
    {
        await EnsureBucketExists(_bucket);
        var auth = await GetInternalToken();
        var ossClient = new OssClient();
        const int PageSize = 64;
        var results = new List<ObjectDetails>();
        var response = await ossClient.GetObjectsAsync(_bucket, PageSize, accessToken: auth.AccessToken);
        results.AddRange(response.Items);
        while (!string.IsNullOrEmpty(response.Next))
        {
            var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(response.Next).Query);
            response = await ossClient.GetObjectsAsync(_bucket, PageSize, startAt: queryParams["startAt"], accessToken: auth.AccessToken);
            results.AddRange(response.Items);
        }
        return results;
    }

    public async Task DeleteObject(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new ArgumentException("Object name cannot be null or empty.", nameof(objectName));
        }

        await EnsureBucketExists(_bucket);
        var auth = await GetInternalToken();
        var ossClient = new OssClient();
        await ossClient.DeleteObjectAsync(_bucket, objectName, accessToken: auth.AccessToken);
    }

    public async Task<string> GetSignedDownloadUrl(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new ArgumentException("Object name cannot be null or empty.", nameof(objectName));
        }

        await EnsureBucketExists(_bucket);
        var auth = await GetInternalToken();

        // APS deprecated direct object download via OSS servers.
        // Use signeds3download to get a short-lived S3 (or OSS fallback) URL.
        // https://aps.autodesk.com/en/docs/data/v2/reference/http/buckets-:bucketKey-objects-:objectKey-signeds3download-GET/
        var baseUrl = $"https://developer.api.autodesk.com/oss/v2/buckets/{Uri.EscapeDataString(_bucket)}/objects/{Uri.EscapeDataString(objectName)}/signeds3download";
        var fileName = Path.GetFileName(objectName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = objectName;
        }

        var url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            baseUrl,
            new Dictionary<string, string>
            {
                // If the upload hasn't been merged yet, this avoids returning multiple chunk URLs.
                ["public-resource-fallback"] = "true",
                ["response-content-type"] = "application/octet-stream",
                ["response-content-disposition"] = $"attachment; filename=\"{fileName}\""
            }
        );

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OSS signeds3download failed ({(int)response.StatusCode} {response.ReasonPhrase}). {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
        {
            var signedUrl = urlEl.GetString();
            if (!string.IsNullOrWhiteSpace(signedUrl))
            {
                return signedUrl;
            }
        }

        // Defensive fallback (should be uncommon with public-resource-fallback=true).
        if (root.TryGetProperty("urls", out var urlsEl) && urlsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in urlsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var firstUrl = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(firstUrl))
                    {
                        return firstUrl;
                    }
                }
            }
        }

        var status = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String
            ? statusEl.GetString()
            : "(missing)";

        throw new InvalidOperationException($"Signed download URL missing in response (status={status}).");
    }
}
