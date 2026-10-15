using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

internal static class DetectorTests
{
    private static readonly List<ClickGesture> result = new List<ClickGesture>();
    private static ClickDetector New() { result.Clear(); return new ClickDetector(360, 1000, result.Add); }
    private static void Expect(string name, params ClickGesture[] expected)
    {
        if (string.Join(",", result) != string.Join(",", expected))
            throw new Exception(name + ": actual=" + string.Join(",", result));
        Console.WriteLine("PASS " + name);
    }
    private static void Click(ClickDetector d, long at) { d.Down(at); d.Up(at + 30); }
    private static void Main(string[] args)
    {
        var d = New(); Click(d, 0); d.Tick(389); Expect("single waits"); d.Tick(390); Expect("single", ClickGesture.Single);
        d = New(); Click(d, 0); Click(d, 200); d.Tick(590); Expect("double excludes single", ClickGesture.Double);
        d = New(); Click(d, 0); Click(d, 200); Click(d, 400); d.Tick(2000); Expect("triple excludes double and single", ClickGesture.Triple);
        d = New(); d.Down(0); d.Tick(999); Expect("no long before one second"); d.Tick(1000); Expect("long fires while still held at one second", ClickGesture.Long);
        for (int i = 1001; i < 30000; i += 33) { d.Down(i); d.Tick(i); } d.Up(30000); d.Tick(31000); Expect("continued hold repeat and release never fire again", ClickGesture.Long);
        d = New(); Click(d, 0); d.Down(200); d.Tick(1199); Expect("pending click waits during second hold"); d.Tick(1200); d.Up(1300); d.Tick(2000); Expect("automatic long cancels pending click", ClickGesture.Long);
        d = New(); d.Down(0); d.Up(1000); d.Tick(2000); Expect("release at threshold before timer still fires once", ClickGesture.Long);
        d = New(); d.Down(0); d.Rotation(); d.Up(1500); d.Tick(2000); Expect("press rotate excludes click and long");
        d = New(); Click(d, 0); d.Reset(); d.Tick(1000); Expect("disconnect cancels pending action");
        d = New(); Click(d, 0); Click(d, 1000); d.Tick(2000); Expect("separated clicks stay single", ClickGesture.Single, ClickGesture.Single);
        d = New(); d.Down(0); d.Tick(500); d.Reset(); d.Tick(1500); d.Up(2000); Expect("disconnect cancels unfinished long hold");
        d = New(); d.Up(0); d.Tick(1000); Expect("unmatched release ignored");
        result.Clear(); d = new ClickDetector(220, 480, 600, result.Add); Click(d, 0); d.Tick(249); Expect("custom single waits for second-click window"); d.Tick(250); Expect("custom single commits", ClickGesture.Single);
        result.Clear(); d = new ClickDetector(220, 480, 600, result.Add); Click(d, 0); Click(d, 200); d.Tick(709); Expect("double waits for separate third-click window"); d.Tick(710); Expect("separate double interval", ClickGesture.Double);
        result.Clear(); d = new ClickDetector(220, 480, 600, result.Add); Click(d, 0); Click(d, 200); Click(d, 650); d.Tick(2000); Expect("slower third click uses its own interval", ClickGesture.Triple);
        if (args.Length > 0)
        {
            d = New();
            var all = File.ReadAllLines(args[0]); int start = 0;
            for (int i = 0; i < all.Length; i++) if (all[i].Contains("READY:")) start = i;
            var pattern = new Regex(@"elapsed_ms=(\d+) KEY (DOWN|UP) vk=0x7C");
            long last = 0;
            for (int i = start; i < all.Length; i++)
            {
                var m = pattern.Match(all[i]); if (!m.Success) continue;
                last = long.Parse(m.Groups[1].Value); d.Tick(last);
                if (m.Groups[2].Value == "DOWN") d.Down(last); else d.Up(last);
            }
            d.Tick(last + 1000);
            Expect("actual device trace with one-second threshold", ClickGesture.Single, ClickGesture.Single,
                ClickGesture.Single, ClickGesture.Single, ClickGesture.Single, ClickGesture.Single, ClickGesture.Long);
        }
    }
}
