using System.Diagnostics; using System.IO.Compression; using System.Text.Json;
if(args.Length<3){Console.Error.WriteLine("Updater <package.zip> <installDir> <mainExe> [waitPid]");return 2;}
var package=Path.GetFullPath(args[0]); var install=Path.GetFullPath(args[1]); var mainExe=args[2]; if(!File.Exists(package)) return 3;
if(args.Length>3&&int.TryParse(args[3],out var pid)){try{using var p=Process.GetProcessById(pid);await p.WaitForExitAsync();}catch{}}
var id=DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff"); var tx=Path.Combine(Path.GetTempPath(),"CreatorControlSuite.Update."+id); var staging=Path.Combine(tx,"staging"); var backup=Path.Combine(tx,"backup"); var journalPath=Path.Combine(tx,"transaction.json"); Directory.CreateDirectory(staging); Directory.CreateDirectory(backup);
var journal=new Journal{TransactionId=id,PackagePath=package,InstallDirectory=install,StartedAt=DateTimeOffset.UtcNow,State="Preparing"}; await Save(journalPath,journal);
try
{
    ZipFile.ExtractToDirectory(package,staging,true); var files=Directory.GetFiles(staging,"*",SearchOption.AllDirectories); journal.State="BackingUp"; await Save(journalPath,journal);
    foreach(var src in files){var rel=Path.GetRelativePath(staging,src);var dest=Path.Combine(install,rel);if(!File.Exists(dest))continue;var bak=Path.Combine(backup,rel);Directory.CreateDirectory(Path.GetDirectoryName(bak)!);File.Copy(dest,bak,true);journal.BackedUpFiles.Add(rel);}
    journal.State="Applying";await Save(journalPath,journal);
    foreach(var src in files){var rel=Path.GetRelativePath(staging,src);var dest=Path.Combine(install,rel);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(src,dest,true);journal.AppliedFiles.Add(rel);}
    journal.State="Completed";journal.CompletedAt=DateTimeOffset.UtcNow;await Save(journalPath,journal);var exe=Path.Combine(install,mainExe);if(File.Exists(exe))Process.Start(new ProcessStartInfo{FileName=exe,WorkingDirectory=install,UseShellExecute=true});return 0;
}
catch(Exception ex)
{
    journal.State="RollingBack";journal.Error=ex.ToString();await Save(journalPath,journal);var errors=new List<string>();
    foreach(var rel in journal.AppliedFiles.AsEnumerable().Reverse()){try{var dest=Path.Combine(install,rel);var bak=Path.Combine(backup,rel);if(File.Exists(bak))File.Copy(bak,dest,true);else if(File.Exists(dest))File.Delete(dest);}catch(Exception rb){errors.Add(rel+": "+rb.Message);}}
    journal.State=errors.Count==0?"RolledBack":"RollbackFailed";journal.RollbackErrors=errors;journal.CompletedAt=DateTimeOffset.UtcNow;await Save(journalPath,journal);Console.Error.WriteLine(ex);return 1;
}
finally{if(journal.State=="Completed"){try{Directory.Delete(tx,true);}catch{}}}
static Task Save(string path,Journal j)=>File.WriteAllTextAsync(path,JsonSerializer.Serialize(j,new JsonSerializerOptions{WriteIndented=true}));
internal sealed class Journal{public string TransactionId{get;set;}="";public string PackagePath{get;set;}="";public string InstallDirectory{get;set;}="";public DateTimeOffset StartedAt{get;set;}public DateTimeOffset? CompletedAt{get;set;}public string State{get;set;}="";public string Error{get;set;}="";public List<string> BackedUpFiles{get;set;}=[];public List<string> AppliedFiles{get;set;}=[];public List<string> RollbackErrors{get;set;}=[];}
