using System;
using System.Collections.Generic;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Peak.Can.Basic;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace CanTestSqlite
{
    public partial class MainViewModel : ObservableObject
    {
        public ObservableCollection<CanMessage> Messages { get; } = new();

        [ObservableProperty] private string status = "Bereit";

        private MultimediaTimer? _timer;
        private bool _isRunning = false;

        public RelayCommand StartCommand { get; }
        public RelayCommand StopCommand { get; }

        public MainViewModel()
        {
            StartCommand = new RelayCommand(Start, () => !_isRunning);
            StopCommand = new RelayCommand(Stop, () => _isRunning);

            LoadMessagesFromDb();
        }

        private void LoadMessagesFromDb()
        {
            Messages.Clear();

            using var conn = new SqliteConnection("Data Source=messages.db");
            conn.Open();

            // Tabelle ggf. anlegen
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS CanMessages (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        CanId       INTEGER NOT NULL,
                        IsExtended  INTEGER NOT NULL DEFAULT 0,
                        Data        TEXT NOT NULL,
                        CycleTimeMs INTEGER NOT NULL DEFAULT 100,
                        Enabled     INTEGER NOT NULL DEFAULT 1
                    );";
                cmd.ExecuteNonQuery();
            }

            using var readCmd = conn.CreateCommand();
            readCmd.CommandText = "SELECT Id, CanId, IsExtended, Data, CycleTimeMs, Enabled FROM CanMessages";
            using var reader = readCmd.ExecuteReader();

            while (reader.Read())
            {
                var msg = new CanMessage
                {
                    Id = reader.GetInt32(0),
                    CanId = reader.GetInt32(1),
                    IsExtended = reader.GetBoolean(2),
                    CycleTimeMs = reader.GetInt32(4),
                    Enabled = reader.GetBoolean(5)
                };
                msg.DataHex = reader.GetString(3); // Setzt über DataHex → null-sicher!
                Messages.Add(msg);
            }

            // Falls leer → Beispiele einfügen
            if (Messages.Count == 0)
            {
                Messages.Add(new CanMessage { CanId = 0x123, DataHex = "11 22 33 44 00 00 00 00", CycleTimeMs = 100, Enabled = true });
                Messages.Add(new CanMessage { CanId = 0x234, DataHex = "11 22 33 44 00 00 00 00", CycleTimeMs = 100, Enabled = true });
                Messages.Add(new CanMessage { CanId = 0x345, DataHex = "11 22 33 44 00 00 00 00", CycleTimeMs = 100, Enabled = true });
                Messages.Add(new CanMessage { CanId = 0x456, IsExtended = true, DataHex = "AA BB CC DD EE FF 00 00", CycleTimeMs = 50, Enabled = true });
                SaveAllToDb();
            }
        }

        private byte[] ParseHexString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new byte[8];

            var parts = s.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new byte[8];
            for (int i = 0; i < parts.Length && i < 8; i++)
            {
                if (byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    result[i] = b;
            }
            return result;
        }

        private void SaveAllToDb()
        {
            using var conn = new SqliteConnection("Data Source=messages.db");
            conn.Open();
            using var transaction = conn.BeginTransaction();

            var deleteCmd = conn.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM CanMessages";
            deleteCmd.ExecuteNonQuery();

            foreach (var msg in Messages)
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO CanMessages (CanId, IsExtended, Data, CycleTimeMs, Enabled) VALUES (@id, @ext, @data, @cycle, @en)";
                cmd.Parameters.AddWithValue("@id", msg.CanId);
                cmd.Parameters.AddWithValue("@ext", msg.IsExtended);
                cmd.Parameters.AddWithValue("@data", msg.DataHex);
                cmd.Parameters.AddWithValue("@cycle", msg.CycleTimeMs);
                cmd.Parameters.AddWithValue("@en", msg.Enabled);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        private void Start()
        {
            var result = PCANBasic.Initialize(PCANBasic.PCAN_USBBUS1, TPCANBaudrate.PCAN_BAUD_500K);
            if (result != TPCANStatus.PCAN_ERROR_OK)
            {
                Status = $"PCAN-Init fehlgeschlagen: {result}";
                return;
            }

            _timer = new MultimediaTimer { Period = 1 };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            _isRunning = true;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            Status = "Läuft – PCAN-USB1 aktiv";

            CommandManager.InvalidateRequerySuggested();
        }

        private void Stop()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;

            PCANBasic.Uninitialize(PCANBasic.PCAN_USBBUS1);

            _isRunning = false;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            Status = "Gestoppt";
            CommandManager.InvalidateRequerySuggested();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var msg in Messages)
            {
                if (!msg.Enabled) continue;

                if (msg.NextSendTime == 0)
                    msg.NextSendTime = now;

                if (now >= msg.NextSendTime)
                {
                    SendMessage(msg);
                    msg.NextSendTime += msg.CycleTimeMs;
                }
            }
        }

        private void SendMessage(CanMessage msg)
        {
            byte[] sourceData = msg.Data ?? new byte[8];
            if (sourceData.Length < 8)
                Array.Resize(ref sourceData, 8);

            var pcanMsg = new TPCANMsg
            {
                ID = (uint)msg.CanId,
                MSGTYPE = msg.IsExtended ? TPCANMessageType.PCAN_MESSAGE_EXTENDED : TPCANMessageType.PCAN_MESSAGE_STANDARD,
                LEN = (byte)Math.Min(sourceData.Length, 8),
                DATA = new byte[8]  // explizit initialisieren → kein Crash mehr!
            };

            Array.Copy(sourceData, pcanMsg.DATA, Math.Min(sourceData.Length, 8));

            PCANBasic.Write(PCANBasic.PCAN_USBBUS1, ref pcanMsg);
        }
    }
}

