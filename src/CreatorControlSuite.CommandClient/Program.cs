using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Ipc;

if(args.Length==0)
{
    Console.Error.WriteLine("Verwendung: CommandClient <command> [key=value]");
    return 2;
}

var arguments=args.Skip(1)
    .Select(x=>x.Split('=',2))
    .Where(x=>x.Length==2)
    .ToDictionary(x=>x[0],x=>x[1],StringComparer.OrdinalIgnoreCase);

var command=new IpcCommand(Guid.NewGuid().ToString("N"),args[0],arguments,DateTimeOffset.Now);

using var pipe=new NamedPipeClientStream(".",NamedPipeIpcServer.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));

try
{
    await pipe.ConnectAsync(timeout.Token);
    using var writer=new StreamWriter(pipe,new UTF8Encoding(false),leaveOpen:true){AutoFlush=true};
    using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
    await writer.WriteLineAsync(JsonSerializer.Serialize(command));
    var line=await reader.ReadLineAsync(timeout.Token);
    var response=JsonSerializer.Deserialize<IpcResponse>(line??"");
    if(response is null) return 3;
    Console.WriteLine(response.Message);
    foreach(var item in response.Data) Console.WriteLine(item.Key+"="+item.Value);
    return response.Success?0:4;
}
catch(Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
