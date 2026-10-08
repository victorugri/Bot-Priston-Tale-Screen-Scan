using System.Runtime.InteropServices.WindowsRuntime;
using BotPriston.Platform.Native;
using BotPriston.Platform.Window;
using OpenCvSharp;
using Serilog;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace BotPriston.Platform.Capture;

/// <summary>
/// Windows.Graphics.Capture backend. Works with DirectX windows and doesn't require the window
/// to be unobstructed (but it must not be minimized). Frames arrive asynchronously; we keep only
/// the newest one and convert it on demand in <see cref="CaptureWindow"/>.
/// </summary>
public sealed class WgcCaptureSource : WindowCaptureSource
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private const int BufferCount = 2;

    private readonly IDirect3DDevice _device;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly TimeSpan _timeout;
    private readonly object _gate = new();
    private readonly AutoResetEvent _frameArrived = new(false);

    private Direct3D11CaptureFrame? _latest;
    private SizeInt32 _poolSize;
    private Mat? _lastImage;
    private byte[] _pixels = [];
    private bool _closed;
    private bool _sizeWarningLogged;

    public WgcCaptureSource(GameWindow window, TimeSpan frameTimeout, ILogger log) : base(window, log)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("Windows.Graphics.Capture is not supported on this system.");

        _timeout = frameTimeout;
        _device = WinRtInterop.CreateDirect3DDevice();
        _item = WinRtInterop.CreateCaptureItemForWindow(window.Handle);
        _item.Closed += (_, _) =>
        {
            _closed = true;
            _frameArrived.Set();
        };

        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, PixelFormat, BufferCount, _poolSize);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(_item);

        // The game draws its own cursor; the system cursor would only pollute detections.
        TrySet(() => _session.IsCursorCaptureEnabled = false, "IsCursorCaptureEnabled");
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            TrySet(() => _session.IsBorderRequired = false, "IsBorderRequired");

        _session.StartCapture();
        Log.Debug("WGC: capture started for {Window}, item size {W}x{H}", window, _poolSize.Width, _poolSize.Height);
    }

    public override string Name => "WGC";

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        var frame = pool.TryGetNextFrame();
        if (frame is null) return;

        if (frame.ContentSize.Width != _poolSize.Width || frame.ContentSize.Height != _poolSize.Height)
        {
            _poolSize = frame.ContentSize;
            pool.Recreate(_device, PixelFormat, BufferCount, _poolSize);
        }

        lock (_gate)
        {
            _latest?.Dispose();
            _latest = frame;
        }
        _frameArrived.Set();
    }

    protected override WindowImage? CaptureWindow()
    {
        if (_closed)
        {
            Log.Warning("WGC: the game window was closed");
            return null;
        }

        var frame = TakeLatest();
        if (frame is null && _lastImage is null)
        {
            // Nothing captured yet: wait for the first frame.
            var deadline = DateTime.UtcNow + _timeout;
            while (frame is null && !_closed && DateTime.UtcNow < deadline)
            {
                _frameArrived.WaitOne(TimeSpan.FromMilliseconds(50));
                frame = TakeLatest();
            }
        }

        if (frame is not null)
        {
            using (frame)
            {
                var image = ToBgrMat(frame);
                _lastImage?.Dispose();
                _lastImage = image;
            }
        }

        // WGC only delivers frames when the window content changes; if nothing new arrived
        // the last converted image is still current.
        if (_lastImage is null)
        {
            Log.Warning("WGC: no frame received within {Timeout} ms (is the window minimized?)", _timeout.TotalMilliseconds);
            return null;
        }

        var screenRect = Window.VisibleFrameRect;
        if (!_sizeWarningLogged && (screenRect.Width != _lastImage.Width || screenRect.Height != _lastImage.Height))
        {
            Log.Warning("WGC: frame size {FW}x{FH} differs from window bounds {Bounds}; client crop may be off",
                _lastImage.Width, _lastImage.Height, screenRect);
            _sizeWarningLogged = true;
        }

        return new WindowImage(_lastImage.Clone(), screenRect);
    }

    private Direct3D11CaptureFrame? TakeLatest()
    {
        lock (_gate)
        {
            var frame = _latest;
            _latest = null;
            return frame;
        }
    }

    private unsafe Mat ToBgrMat(Direct3D11CaptureFrame frame)
    {
        using var bitmap = SoftwareBitmap
            .CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied)
            .AsTask().GetAwaiter().GetResult();

        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        if (_pixels.Length != width * height * 4)
            _pixels = new byte[width * height * 4];
        bitmap.CopyToBuffer(_pixels.AsBuffer());

        var bgr = new Mat();
        fixed (byte* p = _pixels)
        {
            using var bgra = Mat.FromPixelData(height, width, MatType.CV_8UC4, (IntPtr)p);
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        }

        // The surface can be larger than the window content (pool not yet resized).
        var content = frame.ContentSize;
        if (content.Width > 0 && content.Height > 0 && (content.Width < width || content.Height < height))
        {
            var cropped = new Mat(bgr, new Rect(0, 0, Math.Min(content.Width, width), Math.Min(content.Height, height))).Clone();
            bgr.Dispose();
            return cropped;
        }
        return bgr;
    }

    private void TrySet(Action action, string what)
    {
        try { action(); }
        catch (Exception ex) { Log.Debug("WGC: could not set {Property}: {Message}", what, ex.Message); }
    }

    public override void Dispose()
    {
        _session.Dispose();
        _pool.FrameArrived -= OnFrameArrived;
        _pool.Dispose();
        lock (_gate)
        {
            _latest?.Dispose();
            _latest = null;
        }
        _lastImage?.Dispose();
        _device.Dispose();
        _frameArrived.Dispose();
        base.Dispose();
    }
}
