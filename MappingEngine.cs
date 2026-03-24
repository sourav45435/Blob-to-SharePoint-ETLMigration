using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlobToSharePointMigration
{
    public record MappingItem(string SourcePathPrefix, string DestinationPath, string Action);

    public record MappedItem
    {
        public BlobItemDescriptor Source { get; init; } = null!;
        public string DestinationRelativePath { get; init; } = "";
    }

    public class MappingEngine
    {
        private readonly IList<MappingItem> _mappings;
        private readonly ILogger<MappingEngine> _logger;

        public MappingEngine(IConfiguration config, ILogger<MappingEngine> logger)
        {
            _logger = logger;
            var mappingFile = config["Mapping:File"] ?? "mapping.json";
            if (!File.Exists(mappingFile))
            {
                throw new FileNotFoundException("Mapping file not found", mappingFile);
            }

            var content = File.ReadAllText(mappingFile);
            _mappings = JsonSerializer.Deserialize<IList<MappingItem>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? new List<MappingItem>();

            _logger.LogInformation("Loaded {count} mapping entries from {file}", _mappings.Count, mappingFile);
        }

        public IList<MappedItem> ApplyMapping(IList<BlobItemDescriptor> blobs)
        {
            var result = new List<MappedItem>(blobs.Count);

            foreach (var b in blobs)
            {
                var matched = false;
                foreach (var m in _mappings)
                {
                    if (b.BlobPath.StartsWith(m.SourcePathPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var remainder = b.BlobPath.Substring(m.SourcePathPrefix.Length).TrimStart('/', '\\');
                        // Compose destination path: DestinationPath + remainder
                        var dest = string.IsNullOrWhiteSpace(remainder)
                            ? m.DestinationPath
                            : $"{m.DestinationPath.TrimEnd('/', '\\')}/{remainder}";

                        result.Add(new MappedItem { Source = b, DestinationRelativePath = dest.Replace('\\', '/') });
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    // default: keep original path under "Unmapped"
                    result.Add(new MappedItem { Source = b, DestinationRelativePath = $"Unmapped/{b.BlobPath.Replace('\\','/')}" });
                }
            }

            return result;
        }
    }
}