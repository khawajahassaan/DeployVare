using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Transfer;
using API.Services.Interfaces;

namespace API.Services.Implementations;

/// <summary>
/// File storage backed by Backblaze B2 via its S3-compatible API.
/// </summary>
public class B2FileStorageService : IFileStorageService
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;

    public bool IsCloud => true;

    public B2FileStorageService(
        string keyId,
        string appKey,
        string bucket,
        string endpoint,
        string region)
    {
        _bucket = bucket;

        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = region,
        };

        _s3 = new AmazonS3Client(new BasicAWSCredentials(keyId, appKey), config);
    }

    public async Task<string> UploadAsync(string key, Stream data, CancellationToken ct = default)
    {
        var tu = new TransferUtility(_s3);
        await tu.UploadAsync(
            new TransferUtilityUploadRequest
            {
                InputStream = data,
                BucketName = _bucket,
                Key = key,
            },
            ct);

        return key;
    }

    public async Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        var response = await _s3.GetObjectAsync(_bucket, key, ct);
        return response.ResponseStream;
    }
}
