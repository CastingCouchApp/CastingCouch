param([Parameter(Mandatory=$true)][string]$MsiPath,[string]$LogPath=".\clean-install.log")
Set-StrictMode -Version 1.0;$ErrorActionPreference="Stop"
if(-not(Test-Path -LiteralPath $MsiPath)){throw "MSI wurde nicht gefunden: $MsiPath"}
$p=Start-Process -FilePath "msiexec.exe" -ArgumentList @("/i","`"$MsiPath`"","/qn","/norestart","/l*v","`"$LogPath`"") -Wait -PassThru
if($p.ExitCode -ne 0){throw "Neuinstallation fehlgeschlagen. ExitCode: $($p.ExitCode)"}
