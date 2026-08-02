using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SKRMTeam.Services
{
    /// <summary>
    /// Глобальный хук клавиатуры — работает даже когда окно скрыто или не в фокусе
    /// </summary>
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private IntPtr _hookHandle = IntPtr.Zero;
        private readonly NativeMethods.LowLevelKeyboardProc _proc;

        public event Action<Key>? OnKeyDown;

        public KeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Start()
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookHandle = NativeMethods.SetWindowsHookEx(
                WH_KEYBOARD_LL, _proc,
                NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
        }

        public void Stop()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                var key = KeyInterop.KeyFromVirtualKey(vkCode);
                OnKeyDown?.Invoke(key);
            }
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose() => Stop();

        private static class NativeMethods
        {
            public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr GetModuleHandle(string lpModuleName);
        }
    }
}
