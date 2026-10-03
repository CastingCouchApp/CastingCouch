param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$workspace = Join-Path $RepositoryRoot 'artifacts/creator-intelligence-reference'
New-Item -ItemType Directory -Force -Path $workspace | Out-Null
$source = Join-Path $RepositoryRoot 'src/CreatorControlSuite.App/Services/CreatorIntelligence'
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup><Compile Include="Program.cs"/><Compile Include="$source/CreatorIntelligenceModels.cs"/><Compile Include="$source/CreatorIntelligenceService.cs"/><Compile Include="$source/CreatorIntelligenceService.Analysis.cs"/></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $workspace 'Reference.csproj'), $project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/CreatorIntelligenceReference.cs') -Destination (Join-Path $workspace 'Program.cs')
dotnet run --project (Join-Path $workspace 'Reference.csproj') -- (Join-Path $RepositoryRoot 'tauri-app/src-tauri/crates/ccs-modules/tests/fixtures/creator-intelligence.json')
if ($LASTEXITCODE -ne 0) { throw 'C# reference fixture generation failed.' }
