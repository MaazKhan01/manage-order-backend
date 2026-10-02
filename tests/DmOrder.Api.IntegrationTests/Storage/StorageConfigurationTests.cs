using DmOrder.Infrastructure.Storage;
using Shouldly;

namespace DmOrder.Api.IntegrationTests.Storage;

/// <summary>
/// The parts of object storage that can be checked without a bucket.
///
/// These are pure rules, so they need neither the host nor the database - but they live here because
/// this is the only project that can see Infrastructure's internals, and because getting them wrong
/// is how one seller's key reads another seller's object.
/// </summary>
public sealed class StorageKeyTests
{
    [Fact]
    public void A_generated_key_sits_under_its_prefix_and_keeps_only_the_extension()
    {
        var key = StorageKeys.Build("stores/abc/products", "holiday photo.JPG");

        key.ShouldStartWith("stores/abc/products/");
        key.ShouldEndWith(".jpg");
        // The uploaded name is gone entirely - only a generated id remains.
        key.ShouldNotContain("holiday");
    }

    [Fact]
    public void Two_uploads_of_the_same_filename_never_collide()
    {
        var first = StorageKeys.Build("stores/abc/products", "photo.jpg");
        var second = StorageKeys.Build("stores/abc/products", "photo.jpg");

        first.ShouldNotBe(second);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("stores/abc/../../../secrets/key.jpg")]
    [InlineData("/absolute/path.jpg")]
    [InlineData("stores\\abc\\windows.jpg")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_key_we_did_not_generate_is_refused(string hostile)
    {
        // Matters most for object storage: "../" means nothing to S3, so a traversal attempt would
        // not fail on its own - it would quietly address a different object.
        Should.Throw<InvalidOperationException>(() => StorageKeys.Validate(hostile));
    }

    [Fact]
    public void A_generated_key_passes_its_own_validation()
    {
        var key = StorageKeys.Build("stores/abc/storelogo", "logo.png");

        Should.NotThrow(() => StorageKeys.Validate(key));
    }
}

/// <summary>
/// Configuration mistakes that would otherwise surface as every image on every storefront quietly
/// failing to load, hours after a deploy.
/// </summary>
public sealed class S3OptionsTests
{
    private static FileStorageOptions Configured() => new()
    {
        Provider = "S3",
        S3ServiceUrl = "https://account.r2.cloudflarestorage.com",
        S3Bucket = "dmorder-media",
        S3AccessKeyId = "key",
        S3SecretAccessKey = "secret",
        PublicBaseUrl = "https://media.example.com",
    };

    [Fact]
    public void A_fully_configured_bucket_validates()
    {
        Configured().ValidateS3().ShouldBeEmpty();
    }

    [Fact]
    public void The_local_provider_is_not_held_to_the_s3_rules()
    {
        new FileStorageOptions { Provider = "LocalDisk" }.ValidateS3().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(nameof(FileStorageOptions.S3ServiceUrl))]
    [InlineData(nameof(FileStorageOptions.S3Bucket))]
    [InlineData(nameof(FileStorageOptions.S3AccessKeyId))]
    [InlineData(nameof(FileStorageOptions.S3SecretAccessKey))]
    public void Every_required_s3_setting_is_reported_when_missing(string missing)
    {
        var options = Configured();

        switch (missing)
        {
            case nameof(FileStorageOptions.S3ServiceUrl): options.S3ServiceUrl = null; break;
            case nameof(FileStorageOptions.S3Bucket): options.S3Bucket = null; break;
            case nameof(FileStorageOptions.S3AccessKeyId): options.S3AccessKeyId = null; break;
            default: options.S3SecretAccessKey = null; break;
        }

        options.ValidateS3().ShouldContain(result => result.ErrorMessage!.Contains(missing));
    }

    [Fact]
    public void Pointing_the_public_url_at_the_r2_api_endpoint_is_caught()
    {
        // The easiest mistake to make and the hardest to diagnose: the API endpoint requires signed
        // credentials, so every <img> on every storefront would 401 with nothing in our own logs.
        var options = Configured();
        options.PublicBaseUrl = "https://account.r2.cloudflarestorage.com/dmorder-media";

        options.ValidateS3().ShouldContain(result => result.ErrorMessage!.Contains("public"));
    }
}
