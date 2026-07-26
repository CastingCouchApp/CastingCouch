using CreatorControlSuite.Core.Setup;
namespace CreatorControlSuite.Tests;
public sealed class InstallationStateServiceTests
{
 [Fact] public async Task DetectsFirstInstallAndUpgrade()
 {
  var r=Path.Combine(Path.GetTempPath(),"CCS.State",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(r);
  try{var s=new InstallationStateService(Path.Combine(r,"state.json"));
   var f=await s.RegisterStartAsync("1.0.0");Assert.True(f.IsFirstInstall);
   var same=await s.RegisterStartAsync("1.0.0");Assert.False(same.IsUpgrade);
   var u=await s.RegisterStartAsync("2.0.0");Assert.True(u.IsUpgrade);Assert.Equal("1.0.0",u.PreviousVersion);}
  finally{Directory.Delete(r,true);}
 }
}
