using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace BotPriston.Platform.Native;

/// <summary>
/// Minimal COM/WinRT interop needed by Windows.Graphics.Capture from a plain Win32 process:
/// creating a capture item for an HWND and a WinRT Direct3D device.
/// </summary>
internal static unsafe class WinRtInterop
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_IDXGIDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    private const int D3D_DRIVER_TYPE_HARDWARE = 1;
    private const int D3D_DRIVER_TYPE_WARP = 5;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;
    private const int RO_INIT_MULTITHREADED = 1;

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr immediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>Ensures the calling thread is in the MTA. Harmless if already initialized.</summary>
    public static void EnsureInitialized() => RoInitialize(RO_INIT_MULTITHREADED);

    public static GraphicsCaptureItem CreateCaptureItemForWindow(IntPtr hwnd)
    {
        EnsureInitialized();

        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var hstring));
        IntPtr factory;
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, IID_IGraphicsCaptureItemInterop, out factory));
        }
        finally
        {
            WindowsDeleteString(hstring);
        }

        try
        {
            // IGraphicsCaptureItemInterop : IUnknown → slot 3 = CreateForWindow(HWND, REFIID, void**)
            var vtable = *(IntPtr**)factory;
            var createForWindow = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtable[3];

            Guid iid = IID_IGraphicsCaptureItem;
            IntPtr itemPtr;
            Marshal.ThrowExceptionForHR(createForWindow(factory, hwnd, &iid, &itemPtr));
            try
            {
                return GraphicsCaptureItem.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(itemPtr);
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    public static IDirect3DDevice CreateDirect3DDevice()
    {
        EnsureInitialized();

        int hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero, 0, D3D11_SDK_VERSION, out var d3dDevice, out _, out var context);
        if (hr < 0)
        {
            hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_WARP, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero, 0, D3D11_SDK_VERSION, out d3dDevice, out _, out context);
        }
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in IID_IDXGIDevice, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(d3dDevice);
        }
    }
}
