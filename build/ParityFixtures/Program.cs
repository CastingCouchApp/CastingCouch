using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Overlay.Models;
var destination = Path.GetFullPath(args[0]);
Directory.CreateDirectory(destination);
var settings = new AppSettings();
settings.Overlay.EnsureCanvasesMigrated();
File.WriteAllText(Path.Combine(destination, "csharp-settings.json"), JsonSerializer.Serialize(settings, new JsonSerializerOptions {WriteIndented=true}));
File.WriteAllText(Path.Combine(destination, "csharp-overlay.json"), JsonSerializer.Serialize(new OverlayData {UpdatedAt=DateTimeOffset.UnixEpoch}, new JsonSerializerOptions {WriteIndented=true, PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));

Populate(settings,0);
File.WriteAllText(Path.Combine(destination,"csharp-populated-settings.json"),JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));
static void Populate(object value,int depth){
 if(depth>8)return;
 foreach(var property in value.GetType().GetProperties().Where(p=>p.CanRead&&p.GetIndexParameters().Length==0)){
  var child=property.GetValue(value);if(child is null)continue;
  if(child is System.Collections.IList list&&!list.IsReadOnly&&!list.IsFixedSize){
   if(list.Count==0&&property.PropertyType.IsGenericType){var type=property.PropertyType.GetGenericArguments()[0];var item=type==typeof(string)?"Contract":Activator.CreateInstance(type);if(item is not null)list.Add(item);}
   foreach(var item in list)if(item is not null&&item is not string)Populate(item,depth+1);
  }else if(child is System.Collections.IDictionary dictionary){foreach(var item in dictionary.Values)if(item is not null)Populate(item,depth+1);}
  else if(child.GetType().Namespace==typeof(AppSettings).Namespace)Populate(child,depth+1);
 }
}
