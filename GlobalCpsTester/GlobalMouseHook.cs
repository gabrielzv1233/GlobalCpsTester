using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GlobalCpsTester;

internal sealed class GlobalMouseHook : IDisposable
{
    private readonly Action<MouseButtonKind, long> _onClick;
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _hookThread;
    private Native.LowLevelMouseProc? _callback;
    private int _threadId;
    private int _isRunning;
    private string? _lastError;
    private bool _disposed;

    public GlobalMouseHook(Action<MouseButtonKind, long> onClick)
    {
        _onClick = onClick ?? throw new ArgumentNullException(nameof(onClick));
    }

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public string? LastError => Volatile.Read(ref _lastError);

    public bool TryStart()
    {
        ThrowIfDisposed();

        if (_hookThread is { IsAlive: true })
        {
            return IsRunning;
        }

        _ready.Reset();
        Volatile.Write(ref _lastError, null);

        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "GlobalMouseHook"
        };
        _hookThread.Start();

        if (!_ready.Wait(1500))
        {
            Volatile.Write(ref _lastError, "Timed out while starting the global mouse hook.");
            return false;
        }

        return IsRunning;
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        int threadId = Volatile.Read(ref _threadId);
        if (threadId != 0)
        {
            Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (_hookThread is { IsAlive: true })
        {
            _hookThread.Join(1500);
        }

        _hookThread = null;
        Volatile.Write(ref _threadId, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _ready.Dispose();
        _disposed = true;
    }

    private void HookThreadMain()
    {
        IntPtr hookHandle = IntPtr.Zero;

        Volatile.Write(ref _threadId, Native.GetCurrentThreadId());
        Native.EnsureMessageQueue();

        try
        {
            _callback = HookCallback;
            hookHandle = Native.SetHook(_callback);
            if (hookHandle == IntPtr.Zero)
            {
                Volatile.Write(ref _lastError, GetLastErrorMessage());
                return;
            }

            Volatile.Write(ref _isRunning, 1);
        }
        finally
        {
            _ready.Set();
        }

        try
        {
            int result;
            while ((result = Native.GetMessage(out Native.MSG msg, IntPtr.Zero, 0, 0)) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }

            if (result == -1)
            {
                Volatile.Write(ref _lastError, GetLastErrorMessage());
            }
        }
        finally
        {
            if (hookHandle != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hookHandle);
            }

            Volatile.Write(ref _isRunning, 0);
            Volatile.Write(ref _threadId, 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && TryTranslateClick(wParam, lParam, out MouseButtonKind button, out long eventTimeMs))
        {
            _onClick(button, eventTimeMs);
        }

        return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static bool TryTranslateClick(IntPtr wParam, IntPtr lParam, out MouseButtonKind button, out long eventTimeMs)
    {
        Native.MSLLHOOKSTRUCT data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
        eventTimeMs = ExpandMessageTime(data.time);

        switch (wParam.ToInt32())
        {
            case Native.WM_LBUTTONDOWN:
                button = MouseButtonKind.Left;
                return true;
            case Native.WM_RBUTTONDOWN:
                button = MouseButtonKind.Right;
                return true;
            case Native.WM_MBUTTONDOWN:
                button = MouseButtonKind.Middle;
                return true;
            case Native.WM_XBUTTONDOWN:
                button = Native.GetXButton(data.mouseData) == Native.XBUTTON1
                    ? MouseButtonKind.XButton1
                    : MouseButtonKind.XButton2;
                return true;
            default:
                button = default;
                eventTimeMs = 0;
                return false;
        }
    }

    private static long ExpandMessageTime(uint messageTime)
    {
        long nowMs = Environment.TickCount64;
        return nowMs + unchecked((int)(messageTime - (uint)nowMs));
    }

    private static string GetLastErrorMessage()
    {
        return new Win32Exception(Marshal.GetLastWin32Error()).Message;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static class Native
    {
        public const int WH_MOUSE_LL = 14;

        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_XBUTTONDOWN = 0x020B;
        public const uint WM_QUIT = 0x0012;

        public const ushort XBUTTON1 = 0x0001;
        public const ushort XBUTTON2 = 0x0002;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        public static extern int GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostThreadMessage(int idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static ushort GetXButton(uint mouseData)
        {
            return (ushort)(mouseData >> 16);
        }

        public static void EnsureMessageQueue()
        {
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        }

        public static IntPtr SetHook(LowLevelMouseProc callback)
        {
            using Process process = Process.GetCurrentProcess();
            ProcessModule? module = process.MainModule;
            IntPtr moduleHandle = module is null
                ? IntPtr.Zero
                : GetModuleHandle(module.ModuleName);

            return SetWindowsHookEx(WH_MOUSE_LL, callback, moduleHandle, 0);
        }
    }
}