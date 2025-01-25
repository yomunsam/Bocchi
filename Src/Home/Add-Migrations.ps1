param([string] $migration = 'InitialCreate', [string] $migrationProviderName = 'All')

$projectName = "Bocchi.Home.WebHost";
$currentPath = Get-Location
Set-Location "./$projectName"

#Initialze db context and define the target directory
$targetContexts = @{ 
    AppDbContext        = "Migrations/Bocchi";
}

$dbProviders = @{
    Sqlite = "Sqlite"
}

#Fix issue when the tools is not installed and the nuget package does not work see https://github.com/MicrosoftDocs/azure-docs/issues/40048
Write-Host "Updating donet ef tools"
$env:Path += "	% USERPROFILE % \.dotnet\tools";
dotnet tool update --global dotnet-ef

$TargetProjectPath = "../Infrastructure/Bocchi.Home.Infrastructure/Bocchi.Home.Infrastructure.csproj";
Write-Host "[Bocchi]Start migrate projects`n";
foreach ($provider in $dbProviders.Keys) {

    if ($migrationProviderName -eq 'All' -or $migrationProviderName -eq $provider) {
    
        Write-Host "[Bocchi]Generate migration for db provider:" $provider "`n";

        foreach ($context in $targetContexts.Keys) {
                
            $migrationPath = $targetContexts[$context];
            $OutputPath = $migrationPath + "/$provider";

            Write-Host "Migrating context " $context
            # Write-Host "执行 dotnet ef migrations add $migration -c $context -o $OutputPath -p $TargetProjectPath -- --OverrideDbProvider $provider ";
            dotnet ef migrations add $migration -c $context -o $OutputPath -p $TargetProjectPath -- --OverrideDbProvider $provider
            Write-Host "`n"
        } 
        
    }
}

Set-Location $currentPath