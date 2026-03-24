using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlobToSharePointMigration
{
    public class ReportGenerator
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ReportGenerator> _logger;

        public ReportGenerator(IConfiguration config, ILogger<ReportGenerator> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<string> GenerateReportAsync(ManifestResult manifest)
        {
            var outDir = _config["Output:Directory"] ?? "output";
            Directory.CreateDirectory(outDir);

            var report = new
            {
                ManifestBlobPath = manifest.ManifestBlobPath,
                ManifestSasUrl = manifest.ManifestSasUrl.ToString(),
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                TotalFiles = manifest.Entries.Count,
                TotalBytes = manifest.Entries.Sum(e => e.Size)
            };

            var filename = Path.Combine(outDir, $"report-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
            var json = JsonSerializer.Serialize(new { report, entries = manifest.Entries }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filename, json);
            _logger.LogInformation("Wrote report to {file}", filename);
            return filename;
        }
    }
}