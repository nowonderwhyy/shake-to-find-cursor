using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ShakeToFindCursor;

public static class MouseHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const uint WM_QUIT = 0x0012;

    private static readonly LowLevelMouseProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;

    // The hook runs on its own thread with a dedicated message loop. A low-level hook
    // is dispatched on the thread that installed it, so keeping it off the UI thread
    // means UI work (e.g. opening the settings window) can never stall global mouse input.
    private static Thread? _thread;
    private static uint _threadId;
    private static readonly ManualResetEventSlim _ready = new(false);
    private static bool _startResult;

    public static event EventHandler<NativePoint>? MouseMoved;

    /// <summary>Installs the low-level mouse hook on a dedicated thread. Returns false if it could not be set.</summary>
    public static bool Start()
    {
        if (_thread != null) return _hookID != IntPtr.Zero;

        _ready.Reset();
        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "MouseHookThread",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
        _ready.Wait();
        return _startResult;
    }

    public static void Stop()
    {
        if (_thread == null) return;

        if (_hookID != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
        }

        // Wake the message loop so the thread can exit cleanly.
        if (_threadId != 0)
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        _thread.Join(1000);
        _thread = null;
        _threadId = 0;
    }

    private static void ThreadProc()
    {
        _threadId = GetCurrentThreadId();
        _hookID = SetHook(_proc);
        _startResult = _hookID != IntPtr.Zero;
        _ready.Set();

        if (_hookID == IntPtr.Zero) return;

        // Pump messages so the low-level hook callback is delivered on this thread.
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private static IntPtr SetHook(LowLevelMouseProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule? curModule = curProcess.MainModule)
        {
            if (curModule != null)
                return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            return IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
        {
            MSLLHOOKSTRUCT? hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (hookStruct.HasValue)
            {
                MouseMoved?.Invoke(null, hookStruct.Value.pt);
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    public struct NativePoint
    {
        public int X;
        public int Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public NativePoint pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public NativePoint pt;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
