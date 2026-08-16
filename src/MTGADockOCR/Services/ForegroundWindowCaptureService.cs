using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MTGADockOCR.Services;

public sealed class ForegroundWindowCaptureService
{
    public byte[] CapturePng()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || IsIconic(window))
        {
            throw new InvalidOperationException("There is no capturable foreground window.");
        }

        if (!GetWindowRect(window, out var bounds) || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
        {
            throw new InvalidOperationException("The foreground window has invalid bounds.");
        }

        using var image = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(image);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, image.Size, CopyPixelOperation.SourceCopy);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}