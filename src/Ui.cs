internal static class Ui
{
    internal static string Lang = "en";
    internal static string T(string english, string korean) { return Lang == "ko" ? korean : english; }
}
