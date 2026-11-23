using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SKRMTeam.Services
{
    /// <summary>
    /// Загружает актуальные offsets CS2 с GitHub при каждом запуске
    /// Источник: https://github.com/a2x/cs2-dumper
    /// </summary>
    public class OffsetsService
    {
        private readonly HttpClient _httpClient = new();

        // Дефолтные offsets (актуальны на момент правки) на случай если GitHub недоступен.
        // При каждом запуске перезаписываются свежими из cs2-dumper.
        public long DwEntityList { get; private set; } = 40949168;
        public long DwLocalPlayerController { get; private set; } = 38990592;
        public long MSteamID { get; private set; } = 1928;
        public long MIszPlayerName { get; private set; } = 1788;

        public event Action<string>? OnLog;

        public async Task LoadAsync()
        {
            try
            {
                Log("Загружаю актуальные offsets...");

                // Загружаем offsets.json
                string offsetsUrl = "https://raw.githubusercontent.com/a2x/cs2-dumper/main/output/offsets.json";
                var offsetsJson = await _httpClient.GetStringAsync(offsetsUrl);
                var offsets = JObject.Parse(offsetsJson);

                var client = offsets["client.dll"];
                if (client != null)
                {
                    DwEntityList = client["dwEntityList"]?.ToObject<long>() ?? DwEntityList;
                    DwLocalPlayerController = client["dwLocalPlayerController"]?.ToObject<long>() ?? DwLocalPlayerController;
                    Log($"dwEntityList: {DwEntityList}, dwLocalPlayer: {DwLocalPlayerController}");
                }

                // Загружаем client_dll.json для полей класса
                string clientUrl = "https://raw.githubusercontent.com/a2x/cs2-dumper/main/output/client_dll.json";
                var clientJson = await _httpClient.GetStringAsync(clientUrl);
                var clientDll = JObject.Parse(clientJson);

                // CBasePlayerController -> m_steamID, m_iszPlayerName
                var baseCtrl = FindClass(clientDll, "CBasePlayerController");
                if (baseCtrl != null)
                {
                    MSteamID = FieldOffset(baseCtrl, "m_steamID", MSteamID);
                    MIszPlayerName = FieldOffset(baseCtrl, "m_iszPlayerName", MIszPlayerName);
                    Log($"m_steamID: {MSteamID}, m_iszPlayerName: {MIszPlayerName}");
                }

                Log("Offsets обновлены успешно");
            }
            catch (Exception ex)
            {
                Log($"Не удалось обновить offsets, использую дефолтные: {ex.Message}");
            }
        }

        private JObject? FindClass(JObject root, string className)
        {
            foreach (var prop in root.Properties())
            {
                if (prop.Name == className)
                    return prop.Value["fields"] as JObject;

                if (prop.Value is JObject child)
                {
                    var found = FindClass(child, className);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private void Log(string msg) => OnLog?.Invoke($"[OFFSETS] {msg}");

        // cs2-dumper менял формат client_dll.json: раньше поле класса было объектом
        // вида {"offset": N, "type": ...}, сейчас — просто число N. Поддерживаем оба,
        // чтобы не откатываться на битые дефолты при следующем изменении дампа.
        private static long FieldOffset(JObject? fields, string name, long fallback)
        {
            var token = fields?[name];
            if (token is JObject obj)
            {
                var off = obj["offset"];
                return off != null ? off.ToObject<long>() : fallback;
            }
            if (token == null) return fallback;
            return token.ToObject<long>();
        }
    }
}
