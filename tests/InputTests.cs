using System;
using System.Collections.Generic;

internal static class InputTests
{
    private static readonly List<ClickGesture> gestures = new List<ClickGesture>();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static byte[] Report(int usage) { return new byte[] { 5, (byte)usage, (byte)(usage >> 8) }; }
    private static void Feed(ClickDetector detector, int usage, long at)
    {
        switch (KnobInput.Consumer(Report(usage), KnobInput.Receiver))
        {
            case KnobSignal.Press: detector.Down(at); break;
            case KnobSignal.Release: detector.Up(at); break;
            case KnobSignal.Rotation: detector.Rotation(); break;
        }
    }
    private static ClickDetector Detector() { gestures.Clear(); return new ClickDetector(250, 250, 600, gestures.Add); }
    private static void Click(ClickDetector detector, long at) { Feed(detector, 0x53, at); Feed(detector, 0, at + 20); }
    private static void Main()
    {
        Check(KnobInput.Consumer(Report(0x53), KnobInput.Receiver) == KnobSignal.Press && KnobInput.Consumer(Report(0), KnobInput.Receiver) == KnobSignal.Release, "observed receiver reports preserve press and release");
        Check(KnobInput.Consumer(Report(0), KnobInput.Keyboard) == KnobSignal.None, "volume release cannot end an F13 hold");
        Check(KnobInput.Consumer(Report(0x53), KnobInput.Keyboard) == KnobSignal.Rotation, "receiver input is opt-in for existing profiles");
        Check(KnobInput.Consumer(Report(0xe9), KnobInput.Receiver) == KnobSignal.Rotation && KnobInput.Consumer(Report(0xea), KnobInput.Receiver) == KnobSignal.Rotation, "both volume directions cancel a pending gesture");
        Check(KnobInput.Consumer(Report(0xe2), KnobInput.Receiver) == KnobSignal.Rotation, "mute is never mistaken for a receiver click");
        Check(KnobInput.Consumer(new byte[] { 5, 0x53, 1 }, KnobInput.Receiver) != KnobSignal.Press, "consumer usage includes the high byte");
        Check(KnobInput.Consumer(null, KnobInput.Receiver) == KnobSignal.None && KnobInput.Consumer(new byte[] { 5, 0x53 }, KnobInput.Receiver) == KnobSignal.None, "truncated reports are ignored");
        Check(KnobInput.Consumer(new byte[] { 0, 3, 0xfd }, KnobInput.Receiver) == KnobSignal.None && KnobInput.Consumer(new byte[65], KnobInput.Receiver) == KnobSignal.None, "vendor configuration replies cannot trigger gestures");
        var detector = Detector(); Click(detector, 100); detector.Tick(370);
        Check(gestures.Count == 1 && gestures[0] == ClickGesture.Single, "receiver single fires once after its click window");
        detector = Detector(); Click(detector, 100); Click(detector, 250); detector.Tick(520);
        Check(gestures.Count == 1 && gestures[0] == ClickGesture.Double, "receiver double excludes single");
        detector = Detector(); Click(detector, 100); Click(detector, 250); Click(detector, 400);
        Check(gestures.Count == 1 && gestures[0] == ClickGesture.Triple, "receiver triple commits exclusively");
        detector = Detector(); Feed(detector, 0x53, 100); Feed(detector, 0x53, 200); detector.Tick(700); detector.Tick(2000); Feed(detector, 0, 4000); detector.Tick(5000);
        Check(gestures.Count == 1 && gestures[0] == ClickGesture.Long, "receiver hold fires while pressed with no repeat or release action");
        detector = Detector(); Feed(detector, 0x53, 100); Feed(detector, 0xea, 200); Feed(detector, 0, 300); detector.Tick(2000);
        Check(gestures.Count == 0, "press and rotate cancels receiver input");
        detector = Detector(); Feed(detector, 0x53, 100); detector.Reset(); Feed(detector, 0, 200); detector.Tick(2000);
        Check(gestures.Count == 0, "disconnect reset discards the outstanding receiver press");
    }
}
