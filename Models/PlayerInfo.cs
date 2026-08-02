using System.ComponentModel;

namespace SKRMTeam.Models
{
    public class PlayerInfo : INotifyPropertyChanged
    {
        private string _inventoryValue = "...";
        private string _valueColor = "#333333";
        private string _faceitStatus = "FACEIT";
        private string _name = "";
        private double _rawValue = 0;

        public int Index { get; set; }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string SteamId { get; set; } = "";

        // Числовое значение инвентаря для сортировки
        public double RawValue
        {
            get => _rawValue;
            set { _rawValue = value; OnPropertyChanged(nameof(RawValue)); OnPropertyChanged(nameof(IsRich)); }
        }

        // True если у игрока есть FACEIT
        public bool HasFaceit => _faceitStatus != "—" && !string.IsNullOrEmpty(_faceitStatus);

        public string InventoryValue
        {
            get => _inventoryValue;
            set
            {
                _inventoryValue = value;
                if (double.TryParse(value.Replace("$", "").Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    RawValue = val;
                    // Цвет: >=500 красный, >=100 оранжевый, остальное серый
                    ValueColor = val >= 500 ? "#E74C3C"
                               : val >= 100 ? "#FF6B35"
                               : "#555555";
                }
                else
                {
                    RawValue = 0;
                    ValueColor = "#333333";
                }
                OnPropertyChanged(nameof(InventoryValue));
            }
        }

        public string ValueColor
        {
            get => _valueColor;
            set { _valueColor = value; OnPropertyChanged(nameof(ValueColor)); }
        }

        public string FaceitStatus
        {
            get => _faceitStatus;
            set
            {
                _faceitStatus = value;
                OnPropertyChanged(nameof(FaceitStatus));
                OnPropertyChanged(nameof(HasFaceit));
            }
        }

        // True если инвентарь >= $100
        public bool IsRich => _rawValue >= 100;

        // Ссылка на Steam профиль
        public string SteamProfileUrl => $"https://steamcommunity.com/profiles/{SteamId}";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
