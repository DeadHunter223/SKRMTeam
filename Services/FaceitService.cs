using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SKRMTeam.Services
{
    public class FaceitService
    {
        private readonly HttpClient _httpClient = new();
        private string _apiKey = "";

        public event Action<string>? OnLog;

        public void SetApiKey(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            if (!string.IsNullOrWhiteSpace(apiKey))
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        /// <summary>
        /// Проверяет есть ли у игрока FACEIT аккаунт по SteamID64
        /// Возвращает "FACEIT" или "—"
        /// Если есть API ключ — показывает уровень: "LVL 8"
        /// </summary>
        public async Task<string> GetFaceitStatus(string steamId)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                    // Если есть ключ — получаем уровень
                    string url = $"https://open.faceit.com/data/v4/players?game=cs2&game_player_id={steamId}";
                    var response = await _httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        url = $"https://open.faceit.com/data/v4/players?game=csgo&game_player_id={steamId}";
                        response = await _httpClient.GetAsync(url);
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        var json = JObject.Parse(body);
                        int level = json["games"]?["cs2"]?["skill_level"]?.ToObject<int>()
                                 ?? json["games"]?["csgo"]?["skill_level"]?.ToObject<int>()
                                 ?? 0;
                        return level > 0 ? $"LVL {level}" : "FACEIT";
                    }
                    return "—";
                }
                else
                {
                    // Без ключа — просто проверяем есть ли профиль через публичный API
                    string url = $"https://api.faceit.com/search/v1/players?limit=1&game=cs2&game_player_id={steamId}";
                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        var json = JObject.Parse(body);
                        var results = json["payload"]?["results"];
                        if (results != null && results.HasValues)
                        {
                            Log($"FACEIT найден: {steamId}");
                            return "FACEIT";
                        }
                    }
                    return "—";
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка {steamId}: {ex.Message}");
                return "—";
            }
        }

        private void Log(string msg) => OnLog?.Invoke($"[FACEIT] {msg}");
    }
}
