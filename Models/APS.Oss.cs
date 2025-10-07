using System;
using System.Collections.Generic;
using System.IO;
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
}
