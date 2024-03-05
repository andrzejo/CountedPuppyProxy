using System;
using System.IO;
using System.Threading;

namespace PuppyProxy
{
    public class Counter
    {
        private int _ServerBytes;
        private int _ClientBytes;

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

        public bool Modified { get; set; }

        public override string ToString()
        {
            return $"Counter(ServerBytes={ServerBytes}, ClientBytes={ClientBytes})";
        }
    }

    public class TransferCounter
    {
        private Counter _Counter = new Counter();

        private string _Path;

        public TransferCounter(int proxyListenerPort, string settingsRootDir)
        {
            _Path = Path.Combine(settingsRootDir, $"stats-{proxyListenerPort}.json");
            ReadSettings();
            var thread = new Thread(Saver)
            {
                IsBackground = true
            };
            thread.Start();
        }

        private void ReadSettings()
        {
            if (File.Exists(_Path))
            {
                var data = File.ReadAllText(_Path);
                _Counter = Common.DeserializeJson<Counter>(data);
                Console.WriteLine($"Read counter: {_Counter}");
            }
        }

        private void WriteSettings()
        {
            if (_Counter.Modified)
            {
                var json = Common.SerializeJson(_Counter, true);
                File.WriteAllText(_Path, json);
                Console.WriteLine($"Save counter {_Counter} to {_Path}");
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
    }
}