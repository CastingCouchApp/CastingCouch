param([Parameter(Mandatory=$true)][string]$OldMsi,[Parameter(Mandatory=$true)][string]$NewMsi)
Set-StrictMode -Version 1.0;$ErrorActionPreference="Stop"
& (Join-Path $PSScriptRoot "Test-CleanInstall.ps1") -MsiPath $OldMsi -LogPath ".\upgrade-old-install.log"
$p=Start-Process -FilePath "msiexec.exe" -ArgumentList @("/i","`"$NewMsi`"","/qn","/norestart","/l*v","`".\upgrade-new-install.log`"") -Wait -PassThru
if($p.ExitCode -ne 0){throw "Upgrade fehlgeschlagen. ExitCode: $($p.ExitCode)"}
