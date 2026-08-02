using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SKRMTeam.Services
{
    public class MemoryService
    {
        private IntPtr _processHandle = IntPtr.Zero;
        private IntPtr _client = IntPtr.Zero;
        private CancellationTokenSource? _cts;

        // Offsets — обновляются из OffsetsService при старте
        private long _dwEntityList = 39124592;
        private long _dwLocalPlayerController = 37223280;
        private long _mSteamID = 1920;
        private long _mIszPlayerName = 1780;

        public void UpdateOffsets(OffsetsService o)
        {
            _dwEntityList = o.DwEntityList;
            _dwLocalPlayerController = o.DwLocalPlayerController;
            _mSteamID = o.MSteamID;
            _mIszPlayerName = o.MIszPlayerName;
        }

        public event Action<List<(string steamId, string name)>>? OnPlayersFound;
        public event Action<string>? OnLog;
        public event Action<bool>? OnStatusChanged;
        public bool IsRunning { get; private set; }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            IsRunning = true;
            Task.Run(() => ScanLoop(_cts.Token));
            Log("Сканер памяти запущен");
        }

        public void Stop()
        {
            _cts?.Cancel();
            IsRunning = false;
            if (_processHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_processHandle);
                _processHandle = IntPtr.Zero;
            }
            Log("Сканер памяти остановлен");
        }

        private async Task ScanLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Ищем процесс CS2
                    if (!AttachToCS2())
                    {
                        OnStatusChanged?.Invoke(false);
                        await Task.Delay(2000, token);
                        continue;
                    }

                    OnStatusChanged?.Invoke(true);

                    // Читаем игроков
                    var players = ReadPlayers();
                    if (players.Count > 0)
                        OnPlayersFound?.Invoke(players);

                    await Task.Delay(1000, token); // обновляем каждую секунду
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log($"Ошибка сканера: {ex.Message}");
                    await Task.Delay(3000, token);
                }
            }
        }

        private bool AttachToCS2()
        {
            try
            {
                var processes = Process.GetProcessesByName("cs2");
                if (processes.Length == 0) return false;

                var proc = processes[0];

                if (_processHandle == IntPtr.Zero || _client == IntPtr.Zero)
                {
                    _processHandle = NativeMethods.OpenProcess(
                        NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_QUERY_INFORMATION,
                        false, proc.Id);

                    if (_processHandle == IntPtr.Zero) return false;

                    // Получаем базовый адрес client.dll
                    _client = GetModuleBase(proc, "client.dll");
                    if (_client == IntPtr.Zero)
                    {
                        NativeMethods.CloseHandle(_processHandle);
                        _processHandle = IntPtr.Zero;
                        return false;
                    }

                    Log($"CS2 найден! client.dll: 0x{_client:X}");
                }

                return true;
            }
            catch
            {
                _processHandle = IntPtr.Zero;
                _client = IntPtr.Zero;
                return false;
            }
        }

        private List<(string steamId, string name)> ReadPlayers()
        {
            var result = new List<(string, string)>();
            try
            {
                // EntityList ptr
                IntPtr entityListAddr = ReadPointer(_client + (nint)_dwEntityList);
                if (entityListAddr == IntPtr.Zero) return result;

                // Сканируем до 8 chunks
                for (int chunk = 0; chunk < 8; chunk++)
                {
                    // chunk ptr = EntityList + 0x10 + chunk * 8
                    IntPtr chunkPtr = ReadPointer(entityListAddr + (nint)(0x10 + chunk * 8));
                    if (chunkPtr == IntPtr.Zero) continue;

                    // Каждый chunk содержит до 512 entity ptr с шагом 8 байт
                    for (int i = 0; i < 512; i++)
                    {
                        try
                        {
                            IntPtr ctrl = ReadPointer(chunkPtr + (nint)(i * 8));
                            if (ctrl == IntPtr.Zero) continue;

                            ulong steamId64 = ReadULong(ctrl + (nint)_mSteamID);
                            if (steamId64 < 76561197960265728UL || steamId64 > 76561202255233023UL) continue;

                            string name = ReadString(ctrl + (nint)_mIszPlayerName, 128);
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            string sid = steamId64.ToString();
                            if (!result.Exists(p => p.Item1 == sid))
                                result.Add((sid, name));
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка чтения игроков: {ex.Message}");
                NativeMethods.CloseHandle(_processHandle);
                _processHandle = IntPtr.Zero;
                _client = IntPtr.Zero;
            }

            return result;
        }

        private IntPtr GetModuleBase(Process proc, string moduleName)
        {
            foreach (ProcessModule module in proc.Modules)
            {
                if (module.ModuleName?.Equals(moduleName, StringComparison.OrdinalIgnoreCase) == true)
                    return module.BaseAddress;
            }
            return IntPtr.Zero;
        }

        private IntPtr ReadPointer(IntPtr address)
        {
            byte[] buffer = new byte[8];
            if (NativeMethods.ReadProcessMemory(_processHandle, address, buffer, 8, out _))
                return (IntPtr)BitConverter.ToInt64(buffer, 0);
            return IntPtr.Zero;
        }

        private ulong ReadULong(IntPtr address)
        {
            byte[] buffer = new byte[8];
            if (NativeMethods.ReadProcessMemory(_processHandle, address, buffer, 8, out _))
                return BitConverter.ToUInt64(buffer, 0);
            return 0;
        }

        private string ReadString(IntPtr address, int maxLen)
        {
            byte[] buffer = new byte[maxLen];
            if (NativeMethods.ReadProcessMemory(_processHandle, address, buffer, maxLen, out int read))
            {
                int nullIdx = Array.IndexOf(buffer, (byte)0);
                int len = nullIdx >= 0 ? nullIdx : read;
                return Encoding.UTF8.GetString(buffer, 0, len);
            }
            return "";
        }

        private void Log(string msg) => OnLog?.Invoke($"[MEM] {msg}");

        private static class NativeMethods
        {
            public const int PROCESS_VM_READ = 0x0010;
            public const int PROCESS_QUERY_INFORMATION = 0x0400;

            [DllImport("kernel32.dll")]
            public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

            [DllImport("kernel32.dll")]
            public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
                byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

            [DllImport("kernel32.dll")]
            public static extern bool CloseHandle(IntPtr hObject);
        }
    }
}
