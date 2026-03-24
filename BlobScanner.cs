using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlobToSharePointMigration
{
    public class BlobItemDescriptor
    {
        public string BlobName { get; set; } = "";
        public string BlobPath => BlobName;
        public Uri BlobUri { get; set; }
        public long Size { get; set; }
        public DateTimeOffset? CreatedOn { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    public class BlobScanner
    {
        private readonly BlobContainerClient _container;
        private readonly ILogger<BlobScanner> _logger;
        private readonly IConfiguration _config;

        private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".csv", ".html", ".htm" };

        public BlobScanner(IConfiguration config, ILogger<BlobScanner> logger)
        {
            _config = config;
            _logger = logger;

            var connectionString = config["Source:ConnectionString"];
            var containerName = config["Source:Container"];
            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(containerName))
                throw new ArgumentException("Source:ConnectionString and Source:Container must be set in appsettings.json or env variables.");

            _container = new BlobContainerClient(connectionString, containerName);
        }

        public async Task<IList<BlobItemDescriptor>> ListTargetBlobsAsync()
        {
            var results = new List<BlobItemDescriptor>();

            await foreach (var blob in _container.GetBlobsAsync(traits: BlobTraits.Metadata))
            {
                try
                {
                    if (!IsAllowed(blob.Name)) continue;

                    var client = _container.GetBlobClient(blob.Name);
                    var props = await client.GetPropertiesAsync();

                    results.Add(new BlobItemDescriptor
                    {
                        BlobName = blob.Name,
                        BlobUri = client.Uri,
                        Size = props.Value.ContentLength,
                        CreatedOn = props.Value.CreatedOn,
                        LastModified = props.Value.LastModified,
                        Metadata = props.Value.Metadata
                    });
                }
                catch (RequestFailedException rfe)
                {
                    _logger.LogWarning(rfe, "Skipping blob {name} due to request failure", blob.Name);
                }
            }

            return results;
        }

        private static bool IsAllowed(string name)
        {
            var ext = System.IO.Path.GetExtension(name);
            return AllowedExt.Contains(ext);
        }
    }
}