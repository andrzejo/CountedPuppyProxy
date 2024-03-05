using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PuppyProxy
{
    public class Settings
    {
        #region Constructors-and-Factories

        public static Settings FromFile(string filename)
        {
            if (string.IsNullOrEmpty(filename)) throw new ArgumentNullException(nameof(filename));

            var ret = new Settings();

            if (!File.Exists(filename))
            {
                Console.WriteLine("Creating default configuration in " + filename);
                File.WriteAllBytes(filename, Encoding.UTF8.GetBytes(Common.SerializeJson(ret, true)));
                return ret;
            }

            ret = Common.DeserializeJson<Settings>(File.ReadAllBytes(filename));
            return ret;
        }

        #endregion

        #region Public-Members

        public bool EnableConsole { get; set; } = true;

        public SettingsLogging Logging
        {
            get => _Logging;
            set
            {
                if (value == null) _Logging = new SettingsLogging();
                else _Logging = value;
            }
        }

        public string RootDir
        {
            get
            {
                Directory.CreateDirectory(_RootDir);
                return _RootDir;
            }
            set
            {
                _RootDir = string.IsNullOrEmpty(value) ? DefaultRootDir : value;
                Directory.CreateDirectory(_RootDir);
            }
        }

        public SettingsProxy Proxy
        {
            get => _Proxy;
            set
            {
                if (value == null) _Proxy = new SettingsProxy();
                else _Proxy = value;
            }
        }

        public OutputProxy OutputProxy
        {
            get => _OutputProxy;
            set
            {
                if (value == null) _OutputProxy = new OutputProxy();
                else _OutputProxy = value;
            }
        }

        #endregion

        #region Private-Members

        private const string DefaultRootDir = @"c:\PuppyProxy";

        private SettingsLogging _Logging = new SettingsLogging();
        private SettingsProxy _Proxy = new SettingsProxy();
        private OutputProxy _OutputProxy = new OutputProxy();
        private string _RootDir = DefaultRootDir;

        #endregion
    }

    public class SettingsLogging
    {
        private readonly int _MinimumLevel = 0;
        private readonly int _SyslogServerPort = 514;
        public bool SyslogEnable { get; set; } = true;
        public bool ConsoleEnable { get; set; } = true;

        public int MinimumLevel
        {
            get => _MinimumLevel;
            set
            {
                if (value < 0 || value > 7) throw new ArgumentOutOfRangeException(nameof(MinimumLevel));
            }
        }

        public string SyslogServerIp { get; set; } = "127.0.0.1";

        public int SyslogServerPort
        {
            get => _SyslogServerPort;
            set
            {
                if (value < 0 || value > 65535) throw new ArgumentOutOfRangeException(nameof(SyslogServerPort));
            }
        }
    }

    public class SettingsProxy
    {
        private readonly int _ListenerPort = 8000;
        private readonly int _MaxThreads = 256;
        private string _ListenerIpAddress = "127.0.0.1";
        public bool AcceptInvalidCertificates { get; set; } = true;

        public int ListenerPort
        {
            get => _ListenerPort;
            set
            {
                if (value < 0 || value > 65535) throw new ArgumentOutOfRangeException(nameof(ListenerPort));
            }
        }

        public string ListenerIpAddress
        {
            get => _ListenerIpAddress;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(ListenerIpAddress));
                _ListenerIpAddress = IPAddress.Parse(value).ToString();
            }
        }

        public bool Ssl { get; set; } = false;

        public int MaxThreads
        {
            get => _MaxThreads;
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxThreads));
            }
        }
    }

    public class OutputProxy
    {
        public enum ProxyType
        {
            Http,
            Socks4,
            Socks5
        }

        private string _Host;
        private int _Port;

        public int Port
        {
            get => _Port;
            set
            {
                if (value < 0 || value > 65535)
                {
                    throw new ArgumentOutOfRangeException(nameof(_Port));
                }

                _Port = value;
            }
        }

        public string Host { get; set; } = "";

        public string User { get; set; } = "";

        public string Password { get; set; } = "";

        public bool Enabled { get; set; } = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public ProxyType Type { get; set; } = ProxyType.Http;
    }
}