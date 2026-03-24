<#
  Template PowerShell script to create a migration job using PnP.PowerShell.
  Requirements:
    - Install-Module PnP.PowerShell
    - You must have SharePoint Admin rights or appropriate migration permissions
    - The manifest produced by the .NET tool is a JSON file with an array of entries containing SourceUrl and DestinationRelativePath
  Usage:
    pwsh ./submit-migration.ps1 -ManifestSasUrl "<manifest-sas-url>" -TenantAdminUrl "https://<yourtenant>-admin.sharepoint.com" -TargetSite "https://<yourtenant>.sharepoint.com/sites/TargetSite"
#>

param(
    [Parameter(Mandatory=$true)]
    [string] $ManifestSasUrl,

    [Parameter(Mandatory=$true)]
    [string] $TenantAdminUrl,

    [Parameter(Mandatory=$true)]
    [string] $TargetSite,

    [string] $CredentialUsername,
    [string] $CredentialPassword
)

# Connect to SharePoint Admin with PnP.PowerShell using interactive or credential auth.
if ($PSBoundParameters.ContainsKey('CredentialUsername') -and $PSBoundParameters.ContainsKey('CredentialPassword')) {
    $secure = ConvertTo-SecureString $CredentialPassword -AsPlainText -Force
    $cred = New-Object System.Management.Automation.PSCredential ($CredentialUsername, $secure)
    Connect-PnPOnline -Url $TenantAdminUrl -Credentials $cred
} else {
    Connect-PnPOnline -Url $TenantAdminUrl -Interactive
}

# Example: Create migration job from manifest
# NOTE: PnP.PowerShell provides cmdlets for migration; if your version has Invoke-PnPMigrationJob or New-PnPMigrationPackage, use those.
# This is a generic example showing how you might call an API or PnP cmdlet. Adjust to your environment / PnP version.

$manifestLocation = $ManifestSasUrl

Write-Host "Manifest SAS URL: $manifestLocation"
Write-Host "Submitting migration job for target site: $TargetSite"

# PnP.PowerShell sample (if available - cmdlet names may differ between versions)
# The cmdlet below is illustrative. Replace 'Invoke-PnPMigrationJob' with the actual cmdlet in your PnP.PowerShell version.
try {
    # If using Invoke-PnPMigrationJob or Start-PnPMigrationJob
    Invoke-PnPMigrationJob -ManifestUri $manifestLocation -TargetSiteUrl $TargetSite -TenantAdminUrl $TenantAdminUrl -Verbose
}
catch {
    Write-Host "Automatic PnP cmdlet invocation failed, printing manifest and instructions."
    Write-Host "Open the manifest SAS URL in the browser or download it and use the SharePoint Migration API per Microsoft docs:"
    Write-Host "https://learn.microsoft.com/sharepointmigration/introducing-the-sharepoint-migration-api"
}