using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlobToSharePointMigration
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .AddEnvironmentVariables()
                .AddCommandLine(args);

            var configuration = builder.Build();

            using var services = new ServiceCollection()
                .AddLogging(cfg => cfg.AddSimpleConsole(opt => { opt.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] "; }))
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton<BlobScanner>()
                .AddSingleton<MappingEngine>()
                .AddSingleton<PackageGenerator>()
                .AddSingleton<ReportGenerator>()
                .BuildServiceProvider();

            var logger = services.GetRequiredService<ILogger<Program>>();
            try
            {
                logger.LogInformation("Starting Blob->SharePoint Migration preparation...");

                var scanner = services.GetRequiredService<BlobScanner>();
                var mapper = services.GetRequiredService<MappingEngine>();
                var packer = services.GetRequiredService<PackageGenerator>();
                var reporter = services.GetRequiredService<ReportGenerator>();

                // 1. Inventory
                var blobs = await scanner.ListTargetBlobsAsync();

                logger.LogInformation("Discovered {count} blobs.", blobs.Count);

                // 2. Map paths
                var mapped = mapper.ApplyMapping(blobs);
                logger.LogInformation("Applied mapping to {count} items.", mapped.Count);

                // 3. Stage packages to staging container and produce manifest
                var manifest = await packer.StageAndGenerateManifestAsync(mapped);

                logger.LogInformation("Staging complete. Manifest at: {manifestPath}", manifest.ManifestBlobPath);

                // 4. Generate report
                var reportPath = await reporter.GenerateReportAsync(manifest);
                logger.LogInformation("Report generated at: {reportPath}", reportPath);

                logger.LogInformation("Done. Use the included PowerShell script 'submit-migration.ps1' to create the migration job in SharePoint using the manifest SAS URL.");
                return 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fatal error");
                return 1;
            }
        }
    }
}