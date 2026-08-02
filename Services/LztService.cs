using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SKRMTeam.Services
{
    public class LztService
    {
        private readonly HttpClient _httpClient = new();
        private string _token = "";

        public event Action<string>? OnLog;

        public void SetToken(string token)
        {
            _token = token;
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            if (!string.IsNullOrWhiteSpace(token))
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        }

        public async Task<string> GetInventoryValue(string steamId)
        {
            if (string.IsNullOrWhiteSpace(_token))
                return "Нет токена";

            int retries = 0;
            while (retries < 3)
            {
                try
                {
                    string url = $"https://prod-api.lzt.market/steam-value?link={steamId}&app_id=730&currency=usd";
                    var response = await _httpClient.GetAsync(url);

                    // Rate limit — ждём и повторяем
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Log($"Rate limit, жду 3 сек... ({steamId})");
                        await Task.Delay(3000);
                        retries++;
                        continue;
                    }

                    // Закрытый профиль или нет доступа
                    if (response.StatusCode == HttpStatusCode.Forbidden ||
                        response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Log($"Нет доступа к инвентарю: {steamId}");
                        return "Закрыт";
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"HTTP {(int)response.StatusCode} для {steamId}");
                        return "$0.00";
                    }

                    var body = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(body);

                    // Структура: { "data": { "totalValue": 1.38 } }
                    double total = json["data"]?["totalValue"]?.ToObject<double>() ?? 0;
                    Log($"{steamId}: ${total:F2}");
                    return $"${total:F2}";
                }
                catch (TaskCanceledException)
                {
                    Log($"Таймаут для {steamId}");
                    return "$0.00";
                }
                catch (Exception ex)
                {
                    Log($"Ошибка {steamId}: {ex.Message}");
                    retries++;
                    if (retries < 3) await Task.Delay(1000);
                }
            }

            return "$0.00";
        }

        private void Log(string msg) => OnLog?.Invoke($"[LZT] {msg}");
    }
}
