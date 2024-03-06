using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using SyslogLogging;

namespace PuppyProxy
{
    public class TransferCounter
    {
        public Counter Counter { get; internal set; } = new Counter();

        private readonly int _Port;
        private readonly string _RootPath;
        private readonly LoggingModule _Logging;

        public TransferCounter(int proxyListenerPort, string settingsRootDir, LoggingModule logging)
        {
            _Port = proxyListenerPort;
            _RootPath = settingsRootDir;
            _Logging = logging;
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
                Counter = Common.DeserializeJson<Counter>(data);
                _Logging.Debug($"Read counter: {Counter.HumanReadable()} from: {path}");
            }
            else
            {
                Counter.Date = Today();
            }
        }

        private void WriteSettings()
        {
            var path = FilePath();
            var today = Today();
            if (today != Counter.Date)
            {
                _Logging.Info($"Reset counter {Counter.HumanReadable()} to {today}");
                Counter.Reset(today);
            }

            if (Counter.Modified)
            {
                var json = Common.SerializeJson(Counter, true);
                File.WriteAllText(path, json);
                _Logging.Info($"Save counter {Counter.HumanReadable()} to {path}");
                Counter.Modified = false;
            }
        }

        private void Saver(object obj)
        {
            while (true)
            {
                WriteSettings();
                Thread.Sleep(30 * 1000);
            }
        }

        public void IncrementServerBytes(long bytes)
        {
            lock (Counter)
            {
                Counter.ServerBytes += bytes;
            }
        }

        public void IncrementClientBytes(long bytes)
        {
            lock (Counter)
            {
                Counter.ClientBytes += bytes;
            }
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

    public class Counter
    {
        private long _ServerBytes;
        private long _ClientBytes;
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

        public long ServerBytes
        {
            get => _ServerBytes;
            set
            {
                Modified = true;
                _ServerBytes = value;
            }
        }

        public long ClientBytes
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

        private string Format(long size)
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
}