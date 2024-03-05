using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json;

namespace PuppyProxy
{
    public class Counter
    {
        private int _ServerBytes;
        private int _ClientBytes;
        private string _Date;

        public string Date
        {
            get => _Date;
            set
            {
                Modified = true;
                _Date = value;
            }
        }

        public int ServerBytes
        {
            get => _ServerBytes;
            set
            {
                Modified = true;
                _ServerBytes = value;
            }
        }

        public int ClientBytes
        {
            get => _ClientBytes;
            set
            {
                Modified = true;
                _ClientBytes = value;
            }
        }

        [JsonIgnore] public bool Modified { get; set; }

        public void Reset(string today)
        {
            Date = today;
            ServerBytes = 0;
            ClientBytes = 0;
        }

        private string Format(int size)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (size == 0)
            {
                return "0" + suf[0];
            }

            var bytes = Math.Abs(size);
            var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            var num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(size) * num) + suf[place];
        }

        public string HumanReadable()
        {
            return $"Counter(ServerBytes={Format(ServerBytes)}, ClientBytes={Format(ClientBytes)}, Date={Date})";
        }

        public override string ToString()
        {
            return $"Counter(ServerBytes={ServerBytes}, ClientBytes={ClientBytes}, Date={Date})";
        }
    }

    public class TransferCounter
    {
        private Counter _Counter = new Counter();

        private readonly int _Port;
        private readonly string _RootPath;

        public TransferCounter(int proxyListenerPort, string settingsRootDir)
        {
            _Port = proxyListenerPort;
            _RootPath = settingsRootDir;
            ReadSettings();
            var thread = new Thread(Saver)
            {
                IsBackground = true
            };
            thread.Start();
        }

        private void ReadSettings()
        {
            var path = FilePath();
            if (File.Exists(path))
            {
                var data = File.ReadAllText(path);
                _Counter = Common.DeserializeJson<Counter>(data);
                Console.WriteLine($"Read counter: {_Counter.HumanReadable()}");
            }
            else
            {
                _Counter.Date = Today();
            }
        }

        private void WriteSettings()
        {
            var path = FilePath();
            var today = Today();
            if (today != _Counter.Date)
            {
                Console.WriteLine($"Reset counter {_Counter.HumanReadable()} to {today}");
                _Counter.Reset(today);
            }

            if (_Counter.Modified)
            {
                var json = Common.SerializeJson(_Counter, true);
                File.WriteAllText(path, json);
                Console.WriteLine($"Save counter {_Counter.HumanReadable()} to {path}");
                _Counter.Modified = false;
            }
        }

        private void Saver(object obj)
        {
            while (true)
            {
                WriteSettings();
                Thread.Sleep(5 * 1000);
            }
        }

        public void IncrementServerBytes(int bytes)
        {
            _Counter.ServerBytes += bytes;
        }

        public void IncrementClientBytes(int bytes)
        {
            _Counter.ClientBytes += bytes;
        }

        private string FilePath()
        {
            return Path.Combine(_RootPath, $"stats-{_Port}-{Today()}.json");
        }

        private static string Today()
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }
    }
}