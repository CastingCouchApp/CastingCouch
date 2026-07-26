param([Parameter(Mandatory=$true)][string]$ProductCode,[string]$LogPath=".\uninstall.log")
Set-StrictMode -Version 1.0;$ErrorActionPreference="Stop"
$p=Start-Process -FilePath "msiexec.exe" -ArgumentList @("/x",$ProductCode,"/qn","/norestart","/l*v","`"$LogPath`"") -Wait -PassThru
if($p.ExitCode -ne 0){throw "Deinstallation fehlgeschlagen. ExitCode: $($p.ExitCode)"}
