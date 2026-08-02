using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SKRMTeam.Services
{
    /// <summary>
    /// Локальный HTTP сервер для приёма GSI данных от CS2
    /// </summary>
    public class GsiService
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private const int Port = 3000;

        public event Action<JObject>? OnGameStateReceived;
        public event Action<string>? OnLog;

        public bool IsRunning { get; private set; }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            IsRunning = true;
            Log($"GSI сервер запущен на порту {Port}");
            Task.Run(() => ListenLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            IsRunning = false;
            Log("GSI сервер остановлен");
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener!.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    Log($"Ошибка GSI: {ex.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                string body = reader.ReadToEnd();

                context.Response.StatusCode = 200;
                context.Response.Close();

                if (string.IsNullOrWhiteSpace(body)) return;

                var json = JObject.Parse(body);

                // Логируем что получили
                bool hasAllPlayers = json["allplayers"] != null;
                bool hasProvider = json["provider"] != null;
                Log($"Получен пакет — provider:{hasProvider} allplayers:{hasAllPlayers}");

                OnGameStateReceived?.Invoke(json);
            }
            catch (Exception ex)
            {
                Log($"Ошибка разбора GSI: {ex.Message}");
            }
        }

        private void Log(string msg) => OnLog?.Invoke($"[GSI] {msg}");
    }
}
