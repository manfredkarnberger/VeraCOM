using CommunityToolkit.Mvvm.ComponentModel;

namespace CanTestSqlite
{
    public partial class CanMessage : ObservableObject
    {
        public int Id { get; set; }

        [ObservableProperty] private int canId;

        public string CanIdHex
        {
            get => $"0x{CanId:X8}";
            set
            {
                value = value?.TrimStart('0', 'x', 'X') ?? "0";
                if (int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out int v))
                    CanId = v;
                OnPropertyChanged();
            }
        }

        // ──────── ABSOLUT NULL-SICHER ────────
        private byte[] _data = new byte[8]; // Garantiert immer 8 Bytes!

        public byte[] Data
        {
            get => _data;
            private set
            {
                _data = value?.Length > 0
                    ? value.Take(8).Concat(Enumerable.Repeat((byte)0, 8 - value.Length)).ToArray()
                    : new byte[8];
                OnPropertyChanged();
                OnPropertyChanged(nameof(DataHex));
            }
        }

        public string DataHex
        {
            get => string.Join(" ", _data.Select(b => b.ToString("X2")));
            set
            {
                var parts = value?
                    .Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Take(8)
                    .Select(p => byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out byte b) ? b : (byte)0)
                    .ToArray() ?? Array.Empty<byte>();

                var newData = new byte[8];
                Array.Copy(parts, newData, Math.Min(parts.Length, 8));
                Data = newData; // Setter schützt vor null
            }
        }
        // ─────────────────────────────────────

        [ObservableProperty] private bool isExtended;
        [ObservableProperty] private int cycleTimeMs = 100;
        [ObservableProperty] private bool enabled = true;

        public long NextSendTime { get; set; } = 0;
    }
}