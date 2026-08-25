using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace GvrLicense.Infrastructure.Storage;

public sealed class MinioReleaseArtifactStore : IReleaseArtifactStore, IDisposable
{
    private readonly MinioOptions _options;
    private readonly IAmazonS3? _s3;

    public MinioReleaseArtifactStore(IOptions<MinioOptions> options)
    {
        _options = options.Value;
        if (IsConfigured)
        {
            var config = new AmazonS3Config
            {
                ServiceURL = _options.Endpoint.TrimEnd('/'),
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1"
            };
            _s3 = new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
        }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Endpoint) &&
        !string.IsNullOrWhiteSpace(_options.AccessKey) &&
        !string.IsNullOrWhiteSpace(_options.SecretKey) &&
        !string.IsNullOrWhiteSpace(_options.Bucket);

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        EnsureClient();
        var buckets = await _s3!.ListBucketsAsync(ct);
        if (buckets.Buckets.Any(b => string.Equals(b.BucketName, _options.Bucket, StringComparison.Ordinal)))
        {
            return;
        }

        await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, ct);
    }

    public async Task<string> UploadAsync(
        Stream content,
        string version,
        string fileName,
        string contentType,
        CancellationToken ct)
    {
        EnsureClient();
        await EnsureBucketAsync(ct);

        var safeVersion = SanitizeSegment(version);
        var safeName = SanitizeFileName(fileName);
        var objectKey = $"releases/{safeVersion}/{safeName}";

        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true // MinIO path-style a menudo no soporta STREAMING-AWS4-HMAC
        };

        await _s3!.PutObjectAsync(request, ct);
        return objectKey;
    }

    public Task<string> CreatePresignedGetUrlAsync(string objectKey, CancellationToken ct)
    {
        EnsureClient();
        ct.ThrowIfCancellationRequested();

        var expiry = TimeSpan.FromMinutes(Math.Clamp(_options.PresignExpiryMinutes, 5, 24 * 60));
        var url = _s3!.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry)
        });

        return Task.FromResult(url);
    }

    public void Dispose() => _s3?.Dispose();

    private void EnsureClient()
    {
        if (_s3 is null || !IsConfigured)
        {
            throw new InvalidOperationException(
                "MinIO no está configurado. Define Minio__Endpoint, Minio__AccessKey, Minio__SecretKey y Minio__Bucket.");
        }
    }

    private static string SanitizeSegment(string value)
    {
        var trimmed = value.Trim().Replace('\\', '/');
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '-');
        }

        return string.IsNullOrWhiteSpace(trimmed) ? "unknown" : trimmed;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(name) ? "artifact.bin" : SanitizeSegment(name);
    }
}
