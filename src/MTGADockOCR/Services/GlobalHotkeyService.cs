using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MTGADockOCR.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint VirtualKeyD = 0x44;
    private const uint HotkeyMessage = 0x0312;
    private const int HotkeyId = 1;
    private readonly Thread _messageThread;
    private readonly ManualResetEventSlim _started = new();
    private Exception? _startupException;
    private uint _threadId;
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _messageThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "MTGA Dock OCR global hotkey",
        };
        _messageThread.Start();
        _started.Wait();

        if (_startupException is not null)
        {
            Dispose();
            throw new InvalidOperationException("Ctrl+Alt+D could not be registered.", _startupException);
        }
    }

    public event EventHandler? Pressed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero);
        }

        _messageThread.Join(TimeSpan.FromSeconds(2));
        _started.Dispose();
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        try
        {
            if (!RegisterHotKey(IntPtr.Zero, HotkeyId, ModifierControl | ModifierAlt, VirtualKeyD))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _started.Set();
            return;
        }

        _started.Set();
        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == HotkeyMessage && message.WParam == (nuint)HotkeyId)
                {
                    Pressed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr hWnd, uint messageFilterMin, uint messageFilterMax);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}