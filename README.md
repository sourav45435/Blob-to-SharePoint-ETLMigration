# Blob -> SharePoint Migration Preparation Tool

What this tool does:
- Scans an Azure Blob container for PDF/CSV/HTML files.
- Reads basic metadata for each blob.
- Applies a path mapping (mapping.json) to transform source paths into your desired SharePoint hierarchy.
- Stages files into a staging blob container using the transformed path (idempotent copy).
- Generates a manifest JSON and a JSON report suitable for feeding into the SharePoint Migration API or PnP.PowerShell.

NOTES:
- This tool does NOT directly call SharePoint Migration endpoints. Instead, it stages and prepares everything and creates a manifest + SAS URL which you can use with PnP.PowerShell or the Migration REST API. This approach keeps the core .NET tool focused, reliable, and easy to run in long-lived containers.
- The included `submit-migration.ps1` is a template that demonstrates how to submit the manifest using PnP.PowerShell. Adjust it to your tenant and PnP version.

Prerequisites:
- .NET 8 SDK to build (or use the provided Dockerfile)
- Azure Storage access:
  - Source Storage connection string in appsettings.json (Source:ConnectionString)
  - Staging Storage connection string in appsettings.json (Staging:ConnectionString)
  - You can use managed identity + DefaultAzureCredential; for SAS generation you will need a shared key credential on the staging account.

Configuration:
- Edit appsettings.json with your Source and Staging storage details.
- Edit mapping.json to define folder mappings.

Run locally:
1. dotnet build
2. dotnet run --project . (or run the produced executable)

Run in Docker:
1. docker build -t blob-to-sp-migration .
2. docker run -e "ASPNETCORE_ENVIRONMENT=Production" -v $(pwd)/appsettings.json:/app/appsettings.json blob-to-sp-migration

Submitting the manifest to SharePoint:
- After the .NET app completes, copy the manifest SAS URL from the output report.
- Use `submit-migration.ps1` and follow PnP.PowerShell docs to submit a migration job.
- Monitor migration jobs using SharePoint Admin center or PnP cmdlets.

Authentication for Migration API:
- Recommended: register an Azure AD app (service principal) with required SharePoint migration permissions OR use PnP interactive login for POC.
- For production automation, use a tenant-managed service principal and KeyVault to store secrets or use a managed identity and a controlled SAS generation path for the staging container.

If you want, I can:
- Integrate migration job submission from the .NET app (with service principal auth).
- Add checkpointing and resume support for very-large migrations and delta loads.
- Add parallelization with throttling/backoff to improve throughput.
