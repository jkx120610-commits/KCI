using System.Runtime.InteropServices;

namespace KCI.Core;

public sealed class DxgiAdapterInfo
{
    public string Name { get; init; } = string.Empty;
    public uint VendorId { get; init; }
    public uint DeviceId { get; init; }
    public ulong DedicatedVideoMemory { get; init; }
    public ulong DedicatedSystemMemory { get; init; }
    public ulong SharedSystemMemory { get; init; }
    public uint LuidLowPart { get; init; }
    public int LuidHighPart { get; init; }

    public ulong TotalVisibleMemory =>
        DedicatedVideoMemory + SharedSystemMemory + DedicatedSystemMemory;
}

public static class DxgiService
{
    private static readonly Guid FactoryGuid = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public static IReadOnlyList<DxgiAdapterInfo> GetAdapters()
    {
        var result = new List<DxgiAdapterInfo>();
        var factory = IntPtr.Zero;
        try
        {
            var factoryGuid = FactoryGuid;
            if (CreateDXGIFactory1(ref factoryGuid, out factory) != 0 || factory == IntPtr.Zero)
            {
                return result;
            }

            var factoryVtable = Marshal.ReadIntPtr(factory);
            var enumAdapters = Marshal.GetDelegateForFunctionPointer<EnumAdaptersDelegate>(
                Marshal.ReadIntPtr(factoryVtable, IntPtr.Size * 7));

            for (uint index = 0; ; index++)
            {
                if (enumAdapters(factory, index, out var adapter) != 0 || adapter == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    var adapterVtable = Marshal.ReadIntPtr(adapter);
                    var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(
                        Marshal.ReadIntPtr(adapterVtable, IntPtr.Size * 8));
                    if (getDesc(adapter, out var description) != 0)
                    {
                        continue;
                    }

                    result.Add(new DxgiAdapterInfo
                    {
                        Name = description.Description?.Trim('\0', ' ') ?? string.Empty,
                        VendorId = description.VendorId,
                        DeviceId = description.DeviceId,
                        DedicatedVideoMemory = description.DedicatedVideoMemory,
                        DedicatedSystemMemory = description.DedicatedSystemMemory,
                        SharedSystemMemory = description.SharedSystemMemory,
                        LuidLowPart = description.AdapterLuid.LowPart,
                        LuidHighPart = description.AdapterLuid.HighPart
                    });
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        catch
        {
            // DXGI is unavailable in rare remote-desktop or compatibility modes.
        }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                Marshal.Release(factory);
            }
        }
        return result;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdaptersDelegate(IntPtr thisPointer, uint adapter, out IntPtr output);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescDelegate(IntPtr thisPointer, out DxgiAdapterDescription description);

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiLuid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public ulong DedicatedVideoMemory;
        public ulong DedicatedSystemMemory;
        public ulong SharedSystemMemory;
        public DxgiLuid AdapterLuid;
    }
}
