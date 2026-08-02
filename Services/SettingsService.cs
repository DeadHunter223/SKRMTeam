using System;
using System.IO;
using Newtonsoft.Json;

namespace SKRMTeam.Services
{
    public class AppSettings
    {
        public string LztToken { get; set; } = "";
        public string FaceitApiKey { get; set; } = "";
        public bool Topmost { get; set; } = true;
        public string LockKey { get; set; } = "Insert";
        public string HideKey { get; set; } = "Delete";
    }

    public class SettingsService
    {
        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public AppSettings Settings { get; private set; } = new();

        public void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    Settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new();
                }
            }
            catch { Settings = new(); }
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
