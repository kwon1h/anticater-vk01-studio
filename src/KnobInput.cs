internal enum KnobSignal { None, Press, Release, Rotation }

internal static class KnobInput
{
    internal const string Keyboard = "F13";
    internal const string Receiver = "ReceiverTreble";

    // This VK01 receiver sends its ANTICATER "Treble +" mapping as usage 0x0053.
    // Decode only the measured consumer report; vendor configuration replies are not input.
    internal static KnobSignal Consumer(byte[] report, string mode)
    {
        if (report == null || report.Length != 3 || report[0] != 5) return KnobSignal.None;
        int usage = report[1] | (report[2] << 8);
        if (mode == Receiver)
        {
            if (usage == 0x0053) return KnobSignal.Press;
            if (usage == 0) return KnobSignal.Release;
        }
        return usage == 0 ? KnobSignal.None : KnobSignal.Rotation;
    }
}
