using System;
using System.Linq;
internal static class AudioTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    [STAThread] private static void Main(string[] args)
    {
        var selected=new[]{"a","missing","b","c"};var online=new[]{"a","b","c"};
        Check(DeviceCycle.Next(selected,online,"a")=="b","cycle skips disconnected devices");
        Check(DeviceCycle.Next(selected,online,"c")=="a","cycle wraps in user order");
        Check(DeviceCycle.Next(selected,online,"unselected")=="a","external default starts at first selected output");
        Check(DeviceCycle.Next(new[]{"b","a","b"},online,"b")=="a","cycle deduplicates endpoint identifiers");
        bool rejected=false;try{DeviceCycle.Next(new[]{"a","missing"},online,"a");}catch(InvalidOperationException){rejected=true;}Check(rejected,"fewer than two available outputs does not change Windows");
        if(args.Contains("--integration")||args.Contains("--switch-roundtrip"))
        {
            var outputs=AudioDevices.List();foreach(var output in outputs)Console.WriteLine((output.IsDefault?"* ":"  ")+output.Name);
            Check(outputs.Select(x=>x.Id).Distinct().Count()==outputs.Count,"Core Audio returns unique endpoints");
            Check(System.Threading.Tasks.Task.Run(()=>AudioDevices.List().Count).Result==outputs.Count,"Core Audio enumeration works on the background MTA worker");
            if(args.Contains("--switch-roundtrip"))
            {
                var original=Enumerable.Range(0,3).Select(AudioDevices.DefaultId).ToArray();
                var next=outputs.First(x=>x.Id!=original[1]);
                if(original[0]!=original[1]) { Console.WriteLine("SKIP roundtrip: console and multimedia currently differ; preserve existing role routing."); return; }
                try { System.Threading.Tasks.Task.Run(()=>AudioDevices.SetDefault(next.Id,false)).GetAwaiter().GetResult();Check(AudioDevices.DefaultId(0)==next.Id&&AudioDevices.DefaultId(1)==next.Id,"native output switch confirmed from the MTA worker");Check(AudioDevices.DefaultId(2)==original[2],"communications role unchanged when disabled"); }
                finally { AudioDevices.SetDefault(original[1],false); }
                Check(AudioDevices.DefaultId(1)==original[1],"original multimedia output restored");
            }
        }
    }
}
