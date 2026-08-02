using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using SKRMTeam.Models;
using SKRMTeam.Services;

namespace SKRMTeam
{
    public partial class MainWindow : Window
    {
        private readonly GsiService _gsi = new();
        private readonly LztService _lzt = new();
        private readonly FaceitService _faceit = new();
        private readonly OffsetsService _offsets = new();
        private readonly SettingsService _settings = new();
        private readonly MemoryService _memory = new();
        private readonly ObservableCollection<PlayerInfo> _players = new();
        private readonly List<string> _logs = new();
        private readonly HashSet<string> _processedIds = new();
        private readonly KeyboardHook _keyboardHook = new();

        // По умолчанию FALSE — мышь работает нормально
        // Insert переключает в режим игры (click-through)
        private bool _clickThrough = false;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public MainWindow()
        {
            InitializeComponent();
            PlayersList.ItemsSource = _players;

            _settings.Load();
            _lzt.SetToken(_settings.Settings.LztToken);
            _faceit.SetApiKey(_settings.Settings.FaceitApiKey);
            _faceit.OnLog += AddLog;
            _offsets.OnLog += AddLog;
            Topmost = _settings.Settings.Topmost;

            // Загружаем актуальные offsets с GitHub асинхронно
            _ = Task.Run(async () =>
            {
                await _offsets.LoadAsync();
                _memory.UpdateOffsets(_offsets);
            });

            _gsi.OnGameStateReceived += OnGameState;
            _gsi.OnLog += AddLog;
            _lzt.OnLog += AddLog;
            _gsi.Start();
            SetupGsiConfig();

            _memory.OnPlayersFound += OnMemoryPlayers;
            _memory.OnLog += AddLog;
            _memory.OnStatusChanged += OnMemoryStatus;
            _memory.Start();

            _keyboardHook.OnKeyDown += GlobalKeyDown;
            _keyboardHook.Start();

            // Окно запускается в обычном режиме — мышь работает
            // WS_EX_LAYERED нужен для прозрачности фона
            Loaded += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int style = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_LAYERED);
                AddLog("SKRM Team запущен. Insert = режим игры, Delete = скрыть/показать");
            };
        }

        private void ApplyClickThrough(bool enabled)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (enabled)
                // Включаем прозрачность для мыши — играем
                SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);
            else
                // Выключаем — можно кликать по окну
                SetWindowLong(hwnd, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);

            _clickThrough = enabled;

            Dispatcher.Invoke(() =>
            {
                // Меняем цвет рамки — красный = режим игры, тёмный = режим окна
                AddLog(_clickThrough
                    ? "Режим игры — мышь проходит сквозь оверлей"
                    : "Режим окна — можно кликать по оверлею");
            });
        }

        private void GlobalKeyDown(Key key)
        {
            string hideKey = _settings.Settings.HideKey;  // Delete
            string lockKey = _settings.Settings.LockKey;  // Insert

            if (key.ToString() == hideKey)
            {
                Dispatcher.Invoke(() =>
                    Visibility = Visibility == Visibility.Visible
                        ? Visibility.Hidden
                        : Visibility.Visible);
            }
            else if (key.ToString() == lockKey)
            {
                // Insert переключает между режимом игры и режимом окна
                ApplyClickThrough(!_clickThrough);
            }
        }

        private void OnGameState(JObject state)
        {
            try
            {
                string ownId = state["provider"]?["steamid"]?.ToString() ?? "";

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(ownId))
                    {
                        OwnSteamIdText.Text = ownId;
                        GsiIndicator.Fill = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                        GsiStatusText.Text = "GSI активен";
                        GsiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                    }
                });

                var allPlayers = state["allplayers"] as JObject;
                if (allPlayers != null && allPlayers.Count > 0)
                {
                    ProcessPlayers(allPlayers);
                    return;
                }

                if (!string.IsNullOrEmpty(ownId))
                {
                    string ownName = state["player"]?["name"]?.ToString() ?? ownId;
                    AddPlayerIfNew(ownId, ownName);
                }
            }
            catch (Exception ex) { AddLog($"GSI ошибка: {ex.Message}"); }
        }

        private void ProcessPlayers(JObject allPlayers)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var prop in allPlayers.Properties())
                {
                    string name = prop.Value["name"]?.ToString() ?? prop.Name;
                    AddPlayerIfNew(prop.Name, name);
                }
                StatusText.Text = "Сессия активна";
            });
        }

        private void AddPlayerIfNew(string steamId, string name)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var p in _players)
                    if (p.SteamId == steamId) return;

                var player = new PlayerInfo
                {
                    Index = _players.Count + 1,
                    Name = name,
                    SteamId = steamId,
                    InventoryValue = "...",
                    FaceitStatus = "FACEIT"
                };
                _players.Add(player);
                PlayerCountText.Text = _players.Count.ToString();
                PlayerCountDot.Color = Color.FromRgb(0xE7, 0x4C, 0x3C);

                if (!_processedIds.Contains(steamId))
                {
                    _processedIds.Add(steamId);
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(300);
                        // Запрашиваем LZT и FACEIT параллельно
                        var lztTask = _lzt.GetInventoryValue(steamId);
                        var faceitTask = _faceit.GetFaceitStatus(steamId);
                        await Task.WhenAll(lztTask, faceitTask);
                        Dispatcher.Invoke(() =>
                        {
                            player.InventoryValue = lztTask.Result;
                            player.FaceitStatus = faceitTask.Result;
                            SortPlayers();
                        });
                    });
                }
            });
        }

        private void OnMemoryStatus(bool connected)
        {
            Dispatcher.Invoke(() =>
            {
                if (connected)
                {
                    GsiIndicator.Fill = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                    GsiStatusText.Text = "CS2 подключён";
                    GsiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                }
                else
                {
                    GsiIndicator.Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                    GsiStatusText.Text = "Ожидаю CS2...";
                    GsiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                }
            });
        }

        private void OnMemoryPlayers(List<(string steamId, string name)> players)
        {
            foreach (var (steamId, name) in players)
                AddPlayerIfNew(steamId, name);
        }

        private void SetupGsiConfig()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Steam", "steamapps", "common", "Counter-Strike Global Offensive",
                    "game", "csgo", "cfg", "gamestate_integration_skrm.cfg");

                if (!System.IO.File.Exists(path))
                {
                    string dir = System.IO.Path.GetDirectoryName(path)!;
                    if (System.IO.Directory.Exists(dir))
                    {
                        System.IO.File.WriteAllText(path,
                            "\"SKRM Team GSI\"\n{\n" +
                            "    \"uri\" \"http://localhost:3000/\"\n" +
                            "    \"timeout\" \"5.0\"\n" +
                            "    \"buffer\" \"0.1\"\n" +
                            "    \"throttle\" \"0.5\"\n" +
                            "    \"heartbeat\" \"yes\"\n" +
                            "    \"auth\"\n    {\n" +
                            "        \"token\" \"skrmteam2026\"\n    }\n" +
                            "    \"data\"\n    {\n" +
                            "        \"provider\" \"1\"\n" +
                            "        \"map\" \"1\"\n" +
                            "        \"round\" \"1\"\n" +
                            "        \"player_id\" \"1\"\n" +
                            "        \"allplayers_id\" \"1\"\n" +
                            "        \"allplayers_state\" \"1\"\n" +
                            "        \"allplayers_match_stats\" \"1\"\n" +
                            "    }\n}");
                        AddLog("GSI конфиг создан");
                    }
                }
            }
            catch (Exception ex) { AddLog($"GSI конфиг ошибка: {ex.Message}"); }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var sw = new SettingsWindow(_settings);
            sw.Owner = this;
            sw.ShowDialog();
            _lzt.SetToken(_settings.Settings.LztToken);
            _faceit.SetApiKey(_settings.Settings.FaceitApiKey);
            Topmost = _settings.Settings.Topmost;
        }

        private void ResetSession_Click(object sender, RoutedEventArgs e)
        {
            _players.Clear();
            _processedIds.Clear();
            PlayerCountText.Text = "0";
            PlayerCountDot.Color = Color.FromRgb(0x33, 0x33, 0x33);
            StatusText.Text = "Сессия сброшена";
            AddLog("Сессия сброшена вручную");
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _keyboardHook.Stop();
            _memory.Stop();
            _gsi.Stop();
            Application.Current.Shutdown();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();

        private void SortPlayers()
        {
            // Сортировка по убыванию стоимости
            var sorted = _players.OrderByDescending(p => p.RawValue).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                int current = _players.IndexOf(sorted[i]);
                if (current != i) _players.Move(current, i);
                sorted[i].Index = i + 1;
            }
            PlayerCountText.Text = _players.Count.ToString();
        }

        private void OpenProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string url)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                    { UseShellExecute = true });
                AddLog($"Открыт профиль: {url}");
            }
        }

        private void CopySteamId_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string steamId)
            {
                Clipboard.SetText(steamId);
                AddLog($"Скопировано: {steamId}");
            }
        }

        private void HideOverlay_Click(object sender, RoutedEventArgs e)
            => Visibility = Visibility.Hidden;

        private void LockOverlay_Click(object sender, RoutedEventArgs e)
            => ApplyClickThrough(!_clickThrough);

        private void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            _logs.Add(line);
            Dispatcher.Invoke(() => StatusText.Text = msg);
        }

        public List<string> GetLogs() => _logs;
    }
}
