using DmOrder.Infrastructure.Storage;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace DmOrder.Api.Configuration;

/// <summary>
/// Serves locally stored uploads in development.
///
/// Only wired up when the LocalDisk provider is selected. With S3-compatible storage the bucket serves
/// the files directly and the API is not in the path at all.
/// </summary>
public static class LocalMediaHosting
{
    public static WebApplication UseLocalMediaFiles(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value;

        if (!string.Equals(options.Provider, "LocalDisk", StringComparison.OrdinalIgnoreCase))
        {
            return app;
        }

        var root = Path.GetFullPath(options.LocalRootPath);
        Directory.CreateDirectory(root);

        // Only the image types we accept are served, and anything else returns 404 rather than being
        // handed to the browser with a guessed type. An upload that somehow landed here as HTML must
        // never be served as HTML from our own origin.
        var contentTypeProvider = new FileExtensionContentTypeProvider(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".png"] = "image/png",
                [".webp"] = "image/webp",
                [".avif"] = "image/avif",
            });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = "/media",
            ContentTypeProvider = contentTypeProvider,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

                // Storage keys are generated GUIDs, so a file's content never changes under one URL.
                // These two headers make sure a browser cannot be talked into interpreting an image as
                // something executable.
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Context.Response.Headers.ContentDisposition = "inline";
            },
        });

        return app;
    }
}
