using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace BodyImageMigrator;

/// <summary>
/// S3 への画像アップロード。
/// リージョン・バケットは MigrationConfig (development.json) から取得。認証は TSV/コンソール入力または引数で指定。
/// </summary>
public class S3Uploader : IDisposable
{
    /// <summary>接続・リクエストが応答しない場合のタイムアウト（秒）。応答なしでハングしないようにする。</summary>
    private const int RequestTimeoutSeconds = 30;

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly bool _overwrite;
    private readonly bool _disposeClient;

    private static AmazonS3Config CreateConfig(string region)
    {
        return new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(region),
            Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds),
            MaxErrorRetry = 0
        };
    }

    /// <summary>リージョン・バケットを指定して S3 クライアントを生成。認証はデフォルト認証チェーン（未使用の場合は TSV/入力で渡す想定）。</summary>
    public S3Uploader(string region, string bucket, bool overwrite)
    {
        var r = (region ?? "ap-northeast-1").Trim();
        _bucket = (bucket ?? "").Trim();
        if (string.IsNullOrEmpty(_bucket))
            throw new ArgumentException("バケット名を指定してください。", nameof(bucket));
        var config = CreateConfig(r);
        _s3 = new AmazonS3Client(config);
        _overwrite = overwrite;
        _disposeClient = true;
    }

    /// <summary>明示的なアクセスキーとリージョン・バケットで S3 クライアントを生成。</summary>
    public S3Uploader(string accessKeyId, string secretAccessKey, string region, string bucket, bool overwrite)
    {
        var r = (region ?? "ap-northeast-1").Trim();
        _bucket = (bucket ?? "").Trim();
        if (string.IsNullOrEmpty(_bucket))
            throw new ArgumentException("バケット名を指定してください。", nameof(bucket));
        var config = CreateConfig(r);
        var credentials = new BasicAWSCredentials(accessKeyId ?? "", secretAccessKey ?? "");
        _s3 = new AmazonS3Client(credentials, config);
        _overwrite = overwrite;
        _disposeClient = true;
    }

    /// <summary>外部 IAmazonS3 を注入。注入時はこのインスタンスはクライアントを Dispose しない。バケット名は指定必須。</summary>
    public S3Uploader(IAmazonS3 s3, string bucket, bool overwrite)
    {
        _s3 = s3 ?? throw new ArgumentNullException(nameof(s3));
        _bucket = (bucket ?? "").Trim();
        if (string.IsNullOrEmpty(_bucket))
            throw new ArgumentException("バケット名を指定してください。", nameof(bucket));
        _overwrite = overwrite;
        _disposeClient = false;
    }

    /// <summary>
    /// S3 キーを生成する。istcd={ISTCD}/ryono={RYONO}/bodyinfo/{MEMONO}_bg.png または _draw.png
    /// </summary>
    public static string GetS3Key(string istcd, int ryono, long memono, bool isBg)
    {
        var suffix = isBg ? "_bg.png" : "_draw.png";
        return $"istcd={istcd}/ryono={ryono}/bodyinfo/{memono}{suffix}";
    }

    /// <summary>S3 バケットへ接続できるか簡易確認する。認証失敗・ネットワーク不可・タイムアウト時は例外を投げる。</summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = _bucket,
            MaxKeys = 1
        };
        await _s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// オブジェクトが存在するか確認する。
    /// Uses ListObjectsV2 to avoid using exceptions for control flow.
    /// </summary>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = _bucket,
            Prefix = key,
            MaxKeys = 1
        };
        var response = await _s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
        return response.S3Objects != null && response.S3Objects.Any(o => o.Key == key);
    }

    /// <summary>
    /// 画像バイトを S3 にアップロードする。Overwrite が false で既に存在する場合は false を返す。
    /// </summary>
    /// <returns>アップロードした場合 true、既存のためスキップした場合 false</returns>
    public async Task<(bool Uploaded, string? Error)> PutImageAsync(
        string key,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        if (!_overwrite)
        {
            var exists = await ExistsAsync(key, cancellationToken).ConfigureAwait(false);
            if (exists)
                return (false, null);
        }

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var request = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = ms,
                ContentType = "image/png",
                AutoCloseStream = false
            };
            await _s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _s3.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
