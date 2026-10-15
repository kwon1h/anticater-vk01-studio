using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

internal sealed class OutputDevice
{
    public string Id { get; set; }
    public string Name { get; set; }
    public bool IsDefault { get; set; }
}

internal static class DeviceCycle
{
    internal static string Next(IEnumerable<string> selected, IEnumerable<string> available, string current)
    {
        var online = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
        var cycle = selected.Where(online.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cycle.Count < 2) throw new InvalidOperationException("Select at least two connected output devices. / 연결된 출력 장치를 두 개 이상 선택해 주세요.");
        int position = cycle.FindIndex(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase));
        return cycle[(position + 1) % cycle.Count];
    }
}

// Windows COM interop for playback endpoint discovery and default selection.
internal static class AudioDevices
{
    internal static List<OutputDevice> List()
    {
        var enumerator = (IEndpointEnumerator)new EndpointEnumerator(); IEndpointCollection collection = null;
        try
        {
            string current = DefaultId(enumerator, 1);
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out collection));
            uint count; Marshal.ThrowExceptionForHR(collection.GetCount(out count));
            var result = new List<OutputDevice>();
            for (uint i = 0; i < count; i++)
            {
                IEndpoint endpoint = null; IPropertyStore properties = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out endpoint)); string id;
                    Marshal.ThrowExceptionForHR(endpoint.GetId(out id));
                    Marshal.ThrowExceptionForHR(endpoint.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                    PropVariant value; Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out value));
                    string name;
                    try { name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : id; }
                    finally { PropVariantClear(ref value); }
                    result.Add(new OutputDevice { Id = id, Name = name ?? id, IsDefault = string.Equals(id, current, StringComparison.OrdinalIgnoreCase) });
                }
                finally { Release(properties); Release(endpoint); }
            }
            return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally { Release(collection); Release(enumerator); }
    }
    private static string DefaultId(IEndpointEnumerator enumerator, int role)
    {
        IEndpoint endpoint = null;
        try { if (enumerator.GetDefaultAudioEndpoint(0, role, out endpoint) < 0) return null; string id; Marshal.ThrowExceptionForHR(endpoint.GetId(out id)); return id; }
        finally { Release(endpoint); }
    }
    internal static string DefaultId(int role)
    {
        var enumerator = (IEndpointEnumerator)new EndpointEnumerator();
        try { return DefaultId(enumerator, role); } finally { Release(enumerator); }
    }
    internal static string Cycle(Settings settings)
    {
        var devices = List(); var current = devices.FirstOrDefault(x => x.IsDefault);
        string next = DeviceCycle.Next(settings.OutputDeviceIds, devices.Select(x => x.Id), current == null ? null : current.Id);
        SetDefault(next, settings.SwitchCommunications);
        return devices.First(x => x.Id == next).Name;
    }
    internal static void SetDefault(string id, bool communications)
    {
        if (!List().Any(x => x.Id == id)) throw new InvalidOperationException("Output is disconnected. / 출력 장치가 연결되어 있지 않습니다.");
        var policy = (IEndpointPolicy)new EndpointPolicy();
        var roles = communications ? new[] { 0, 1, 2 } : new[] { 0, 1 };
        var previous = roles.ToDictionary(x => x, DefaultId);
        try
        {
            foreach (int role in roles) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
            foreach (int role in roles) if (!string.Equals(DefaultId(role), id, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Windows did not confirm the output change. / Windows에서 출력 변경을 확인하지 못했습니다.");
        }
        catch
        {
            foreach (int role in roles) if (previous[role] != null) { try { policy.SetDefaultEndpoint(previous[role], role); } catch { } }
            throw;
        }
        finally { Release(policy); }
    }
    private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { internal Guid Format; internal uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] internal ushort Type; [FieldOffset(8)] internal IntPtr Pointer; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class EndpointEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IEndpointCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IEndpoint device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IEndpoint device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IEndpoint device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpoint
    {
        [PreserveSig] int Activate(ref Guid id, uint context, IntPtr activation, out IntPtr result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] private class EndpointPolicy { }
    // Preserve all preceding vtable slots; only the default endpoint method is invoked.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointPolicy
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int fallback, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpoint, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int fallback, IntPtr value, IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr value);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr value);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr value);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int store, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int store, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
