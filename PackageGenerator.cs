using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq;
using Azure;

namespace BlobToSharePointMigration
{
    public record ManifestEntry(string SourceUrl, string DestinationRelativePath, long Size, DateTimeOffset? Created, DateTimeOffset? Modified, IDictionary<string, string> Metadata);
    public record ManifestResult(string ManifestBlobPath, Uri ManifestSasUrl, IList<ManifestEntry> Entries);

    public class PackageGenerator
    {
        private readonly BlobContainerClient _sourceContainer;
        private readonly BlobContainerClient _stagingContainer;
        private readonly ILogger<PackageGenerator> _logger;
        private readonly IConfiguration _config;

        public PackageGenerator(IConfiguration config, ILogger<PackageGenerator> logger)
        {
            _config = config;
            _logger = logger;

            var srcConn = config["Source:ConnectionString"];
            var srcContainer = config["Source:Container"];
            var stagingConn = config["Staging:ConnectionString"];
            var stagingContainer = config["Staging:Container"];

            if (string.IsNullOrWhiteSpace(srcConn) || string.IsNullOrWhiteSpace(srcContainer) ||
                string.IsNullOrWhiteSpace(stagingConn) || string.IsNullOrWhiteSpace(stagingContainer))
            {
                throw new ArgumentException("Source and Staging connection strings and container names must be configured.");
            }

            _sourceContainer = new BlobContainerClient(srcConn, srcContainer);
            _stagingContainer = new BlobContainerClient(stagingConn, stagingContainer);
            _stagingContainer.CreateIfNotExists();
        }

        public async Task<ManifestResult> StageAndGenerateManifestAsync(IList<MappedItem> mappedItems)
        {
            var manifestEntries = new List<ManifestEntry>();

            // For each mapped item, copy to staging container under a normalized path and record the staging URL as source for migration
            foreach (var item in mappedItems)
            {
                try
                {
                    var sourceClient = _sourceContainer.GetBlobClient(item.Source.BlobName);

                    // Destination blob path within staging: use transformed destination path (replace spaces if needed)
                    var dstName = NormalizeForBlob(item.DestinationRelativePath);
                    var dstClient = _stagingContainer.GetBlobClient(dstName);

                    // If the destination already exists and sizes match, skip copy (idempotence)
                    var skip = false;
                    try
                    {
                        var dstProps = await dstClient.GetPropertiesAsync();
                        if (dstProps.Value.ContentLength == item.Source.Size)
                        {
                            _logger.LogDebug("Skipping copy for {blob} because staged copy exists and size matches.", dstName);
                            skip = true;
                        }
                    }
                    catch (RequestFailedException)
                    {
                        // doesn't exist
                    }

                    if (!skip)
                    {
                        // Use StartCopyFromUri (server-side copy) for speed if same storage account; fallback to download/upload
                        var copy = await dstClient.StartCopyFromUriAsync(sourceClient.Uri);
                        // Optionally wait for copy to complete or assume eventual consistency; here we'll wait for completion in a simple loop
                        var poll = 0;
                        while (true)
                        {
                            var props = await dstClient.GetPropertiesAsync();
                            if (props.Value.CopyStatus != CopyStatus.Pending) break;
                            await Task.Delay(500);
                            poll++;
                            if (poll % 20 == 0) _logger.LogDebug("Waiting for copy of {dst}", dstName);
                        }
                    }

                    // Create SAS for staged blob (short-lived, used in manifest)
                    var sasUri = GenerateBlobSasUri(dstClient);

                    manifestEntries.Add(new ManifestEntry(
                        SourceUrl: sasUri.ToString(),
                        DestinationRelativePath: item.DestinationRelativePath,
                        Size: item.Source.Size,
                        Created: item.Source.CreatedOn,
                        Modified: item.Source.LastModified,
                        Metadata: item.Source.Metadata
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error staging {blob}", item.Source.BlobName);
                }
            }

            // Build manifest JSON (simple schema: list of entries). The official Migration API expects a particular package schema; this manifest is intentionally simple and the included PowerShell template will demonstrate how to pass it to PnP.PowerShell or the migration API.
            var manifestObj = new { GeneratedAtUtc = DateTimeOffset.UtcNow, Entries = manifestEntries };
            var manifestJson = JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true });

            var manifestName = $"migration-manifests/manifest-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json";
            var manifestBlob = _stagingContainer.GetBlobClient(manifestName);
            await manifestBlob.UploadAsync(BinaryData.FromString(manifestJson), overwrite: true);

            var manifestSas = GenerateBlobSasUri(manifestBlob, minutes: 60 * 24); // default 24h; increase if you need longer
            return new ManifestResult(manifestBlobPath: manifestName, manifestSasUrl: manifestSas, Entries: manifestEntries);
        }

        private static string NormalizeForBlob(string path)
        {
            // Replace invalid characters, trim, etc.
            var s = path.Trim('/');
            s = s.Replace('\\', '/');
            // optional: replace spaces with underscores
            s = string.Join("/", s.Split('/').Select(part => Uri.EscapeDataString(part)));
            return s;
        }

        private static Uri GenerateBlobSasUri(BlobClient blobClient, int minutes = 60)
        {
            // This method assumes that the client was created with a StorageSharedKeyCredential (connection string) and therefore supports generating SAS locally.
            // If not available (e.g., using Managed Identity) you should create SAS via a separate SAS generation service or grant the migration service access to the container.
            if (!blobClient.CanGenerateSasUri)
            {
                throw new InvalidOperationException("BlobClient cannot generate SAS. Ensure the staging connection string used allows SAS generation (shared key).");
            }

            var sas = blobClient.GenerateSasUri(Azure.Storage.Sas.BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(minutes));
            return sas;
        }
    }
}