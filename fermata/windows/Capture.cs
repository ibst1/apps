using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Fermata;

/// <summary>
/// Captures one window with Windows Graphics Capture: WITHOUT the mouse pointer and without
/// the yellow border, also while other windows (the presenter view) lie on top of it.
/// Frames are copied to the CPU and JPEG-encoded on a few worker threads, then handed to
/// <see cref="FrameReady"/> strictly in capture order (a frame that finishes early waits for the
/// one before it), together with the time Windows captured it – so animations keep an even pace.
/// </summary>
sealed class WindowCapture : IDisposable
{
    public event Action<byte[], int, int, long> FrameReady;   // jpeg, width, height, capture time (µs)
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Fps => _fps;

    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly IDirect3DDevice _winrtDevice;
    readonly GraphicsCaptureItem _item;
    Direct3D11CaptureFramePool _pool;
    GraphicsCaptureSession _session;
    ID3D11Texture2D _staging;
    Windows.Graphics.SizeInt32 _size;
    readonly object _lock = new();
    long _seq, _nextOut = 1;
    readonly Dictionary<long, (byte[] jpeg, int w, int h, long ts)> _done = new();   // encoded, waiting for their turn
    int _busy, _fps, _frames;
    DateTime _fpsStart = DateTime.UtcNow;
    long _lastFrameTicks;
    const int Workers = 3;
    const int MaxWidth = 1920, MaxHeight = 1200;   // like the browser path: larger screens are scaled down
    readonly ImageCodecInfo _jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");

    public WindowCapture(IntPtr hwnd)
    {
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null, out _device, out _context).CheckError();
        using (var dxgi = _device.QueryInterface<IDXGIDevice>())
        {
            Marshal.ThrowExceptionForHR((int)CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var p));
            _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(p);
            Marshal.Release(p);
        }
        _item = CreateItemForWindow(hwnd);
        _size = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = false;                // the whole point: no pointer in the picture
        // Windows 11: no yellow border around the slideshow (Windows 10 always draws one)
        if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            try { _session.IsBorderRequired = false; } catch (Exception e) { Log.Write("border: " + e.Message); }
        _pool.FrameArrived += OnFrame;
        _session.StartCapture();
        Log.Write($"Capture started {_size.Width}x{_size.Height}");
    }

    // Diagnostics: frames Windows delivered / skipped because encoders were busy, per log period
    int _arrived, _skippedBusy;
    DateTime _statsStart = DateTime.UtcNow;
    public string TakeStats()
    {
        var s = (DateTime.UtcNow - _statsStart).TotalSeconds;
        var r = $"arrived {_arrived / s:0.0}/s, skipped(busy) {_skippedBusy / s:0.0}/s, sent {_fps}/s";
        _arrived = 0; _skippedBusy = 0; _statsStart = DateTime.UtcNow;
        return r;
    }

    void OnFrame(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame == null) return;
        Interlocked.Increment(ref _arrived);
        var size = frame.ContentSize;
        if (size.Width != _size.Width || size.Height != _size.Height)
        {
            _size = size;
            sender.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            return;                                             // next frame has the new size
        }
        // Max 60 frames per second, and skip while all encoders are busy (newest frame wins)
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastFrameTicks < TimeSpan.TicksPerSecond / 61) return;
        if (Volatile.Read(ref _busy) >= Workers) { Interlocked.Increment(ref _skippedBusy); return; }
        _lastFrameTicks = now;

        byte[] bgra; int w, h, stride;
        lock (_lock)
        {
            using var tex = TextureFromSurface(frame.Surface);
            var desc = tex.Description;
            w = Math.Min((int)desc.Width, size.Width); h = Math.Min((int)desc.Height, size.Height);
            if (_staging == null || _staging.Description.Width != desc.Width || _staging.Description.Height != desc.Height)
            {
                _staging?.Dispose();
                desc.Usage = ResourceUsage.Staging; desc.BindFlags = BindFlags.None;
                desc.CPUAccessFlags = CpuAccessFlags.Read; desc.MiscFlags = ResourceOptionFlags.None;
                _staging = _device.CreateTexture2D(desc);
            }
            _context.CopyResource(_staging, tex);
            var map = _context.Map(_staging, 0, MapMode.Read);
            try
            {
                stride = w * 4; bgra = new byte[stride * h];
                for (int y = 0; y < h; y++)
                    Marshal.Copy(map.DataPointer + y * (int)map.RowPitch, bgra, y * stride, stride);
            }
            finally { _context.Unmap(_staging, 0); }
        }
        long ts = frame.SystemRelativeTime.Ticks / 10;          // when Windows captured it, in µs
        long seq = Interlocked.Increment(ref _seq);
        Interlocked.Increment(ref _busy);
        ThreadPool.QueueUserWorkItem(_ => Encode(seq, bgra, w, h, stride, ts));
    }

    void Encode(long seq, byte[] bgra, int w, int h, int stride, long ts)
    {
        byte[] jpeg = null; int ow = w, oh = h;
        try
        {
            var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                using var bmp = new Bitmap(w, h, stride, PixelFormat.Format32bppRgb, handle.AddrOfPinnedObject());
                Bitmap toSave = bmp;
                double s = Math.Min(1.0, Math.Min((double)MaxWidth / w, (double)MaxHeight / h));
                if (s < 1.0)
                {
                    ow = (int)(w * s) & ~1; oh = (int)(h * s) & ~1;
                    toSave = new Bitmap(ow, oh, PixelFormat.Format24bppRgb);
                    using var g = Graphics.FromImage(toSave);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(bmp, 0, 0, ow, oh);
                }
                using var ms = new MemoryStream(400_000);
                var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(Encoder.Quality, 92L);   // sharp text, small enough for 60 fps
                toSave.Save(ms, _jpeg, ep);
                if (!ReferenceEquals(toSave, bmp)) toSave.Dispose();
                jpeg = ms.ToArray();
            }
            finally { handle.Free(); }
        }
        catch (Exception e) { Log.Write("encode: " + e.Message); jpeg = null; }
        // Hand frames on in capture order: this one may have to wait for an earlier one still encoding.
        // Invoked inside the lock so two threads can never hand frames on out of order
        // (the handler only queues the send, so this is quick).
        lock (_lock)
        {
            _done[seq] = (jpeg, ow, oh, ts);
            while (_done.Remove(_nextOut, out var f))
            {
                _nextOut++;
                if (f.jpeg == null) continue;                    // failed to encode: skip it, keep the order
                Width = f.w; Height = f.h;
                _frames++;
                var el = (DateTime.UtcNow - _fpsStart).TotalSeconds;
                if (el >= 2) { _fps = (int)Math.Round(_frames / el); _frames = 0; _fpsStart = DateTime.UtcNow; }
                try { FrameReady?.Invoke(f.jpeg, f.w, f.h, f.ts); } catch (Exception e) { Log.Write("frame handler: " + e.Message); }
            }
        }
        Interlocked.Decrement(ref _busy);
    }

    ID3D11Texture2D TextureFromSurface(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = typeof(ID3D11Texture2D).GUID;
        var p = access.GetInterface(ref iid);
        return new ID3D11Texture2D(p);
    }

    public void Dispose()
    {
        try { _pool.FrameArrived -= OnFrame; } catch { }
        try { _session?.Dispose(); } catch { }
        try { _pool?.Dispose(); } catch { }
        lock (_lock) { _staging?.Dispose(); _staging = null; }
        _context?.Dispose(); _device?.Dispose();
        Log.Write("Capture stopped");
    }

    // ---- interop
    static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var guid = GraphicsCaptureItemGuid;
        var p = interop.CreateForWindow(hwnd, ref guid);
        var item = GraphicsCaptureItem.FromAbi(p);
        Marshal.Release(p);
        return item;
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
