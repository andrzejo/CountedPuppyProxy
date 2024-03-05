using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RestWrapper;
using Starksoft.Aspen.Proxy;
using SyslogLogging;
using WatsonWebserver;
using HttpMethod = WatsonWebserver.HttpMethod;

namespace PuppyProxy
{
    internal class MainClass
    {
        private static string _SettingsFile;
        private static Settings _Settings;
        private static LoggingModule _Logging;

        private static TunnelManager _Tunnels;
        private static SecurityModule _SecurityModule;

        private static TcpListener _TcpListener;

        private static CancellationTokenSource _CancelTokenSource;
        private static CancellationToken _CancelToken;
        private static int _ActiveThreads;

        private static readonly EventWaitHandle Terminator = new EventWaitHandle(false, EventResetMode.ManualReset);
        private static TransferCounter _Counter;

        private static void Main(string[] args)
        {
            #region Setup

            LoadConfiguration(args);
            Welcome();

            #endregion

            #region Initialize-Globals

            _Logging = new LoggingModule(
                _Settings.Logging.SyslogServerIp,
                _Settings.Logging.SyslogServerPort,
                _Settings.Logging.ConsoleEnable,
                (LoggingModule.Severity)_Settings.Logging.MinimumLevel,
                false,
                true,
                true,
                false,
                true,
                false);

            _Tunnels = new TunnelManager(_Logging);
            _SecurityModule = new SecurityModule(_Logging);

            _CancelTokenSource = new CancellationTokenSource();
            _CancelToken = _CancelTokenSource.Token;

            Task.Run(() => AcceptConnections(), _CancelToken);

            #endregion

            #region Console

            if (_Settings.EnableConsole)
            {
                var userInput = "";
                var runForever = true;

                while (runForever)
                {
                    userInput = Common.InputString("Command [? for help] >", null, false);
                    switch (userInput.ToLower().Trim())
                    {
                        case "?":
                            Console.WriteLine("");
                            Console.WriteLine("--- Available Commands ---");
                            Console.WriteLine(" ?           Help, this menu");
                            Console.WriteLine(" c/cls       Clear the screen");
                            Console.WriteLine(" q/quit      Exit PuppyProxy");
                            Console.WriteLine(" tunnels     List current CONNECT tunnels");
                            Console.WriteLine(" stats       List transfer stats");
                            Console.WriteLine("");
                            break;

                        case "c":
                        case "cls":
                            Console.Clear();
                            break;

                        case "q":
                        case "quit":
                            Terminator.Set();
                            _CancelTokenSource.Cancel();
                            runForever = false;
                            break;

                        case "tunnels":
                            var tunnels = _Tunnels.GetMetadata();
                            if (tunnels != null && tunnels.Count > 0)
                                foreach (var curr in tunnels)
                                    Console.WriteLine(" ID " + curr.Key + ": " + curr.Value);
                            else
                                Console.WriteLine("None");

                            break;
                        case "stats":
                            var stats = _Counter.Counter;
                            Console.WriteLine("Transfer stats:");
                            Console.WriteLine($"  {stats.HumanReadable()}");
                            Console.WriteLine($"  {stats}");

                            break;
                    }
                }
            }

            #endregion

            Terminator.WaitOne();
        }

        #region Setup-Methods

        private static void Welcome()
        {
            Console.WriteLine(
                Constants.Logo +
                "PuppyProxy starting on " + _Settings.Proxy.ListenerIpAddress + ":" + _Settings.Proxy.ListenerPort);

            if (string.IsNullOrEmpty(_SettingsFile))
                Console.WriteLine("Use --cfg=<filename> to load from a configuration file");
        }

        private static void LoadConfiguration(string[] args)
        {
            var display = false;
            _SettingsFile = null;

            if (args != null && args.Length > 0)
                foreach (var curr in args)
                    if (curr.StartsWith("--cfg="))
                        _SettingsFile = curr.Substring(6);
                    else if (curr.Equals("--display-cfg")) display = true;

            if (!string.IsNullOrEmpty(_SettingsFile))
                _Settings = Settings.FromFile(_SettingsFile);
            else
                _Settings = new Settings();

            if (display)
            {
                Console.WriteLine("--- Configuration ---");
                Console.WriteLine(Common.SerializeJson(_Settings, true));
                Console.WriteLine("");
            }
        }

        #endregion

        #region Connection-Handler

        private static void AcceptConnections()
        {
            try
            {
                if (string.IsNullOrEmpty(_Settings.Proxy.ListenerIpAddress))
                    _TcpListener = new TcpListener(IPAddress.Any, _Settings.Proxy.ListenerPort);
                else
                    _TcpListener = new TcpListener(IPAddress.Parse(_Settings.Proxy.ListenerIpAddress),
                        _Settings.Proxy.ListenerPort);

                _Counter = new TransferCounter(_Settings.Proxy.ListenerPort, _Settings.RootDir, _Logging);
                _TcpListener.Start();

                while (!_CancelToken.IsCancellationRequested)
                {
                    var client = _TcpListener.AcceptTcpClient();
                    Task.Run(() => ProcessConnection(client), _CancelToken);
                }
            }
            catch (Exception eOuter)
            {
                _Logging.Exception("PuppyProxy", "AcceptConnections", eOuter);
            }
        }

        private static async Task ProcessConnection(TcpClient client)
        {
            var clientIp = "";
            var clientPort = 0;
            var connectionId = Thread.CurrentThread.ManagedThreadId;
            _ActiveThreads++;

            try
            {
                #region Check-if-Max-Exceeded

                if (_ActiveThreads >= _Settings.Proxy.MaxThreads)
                {
                    _Logging.Warn("AcceptConnections connection count " + _ActiveThreads + " exceeds configured max " +
                                  _Settings.Proxy.MaxThreads + ", waiting");

                    while (_ActiveThreads >= _Settings.Proxy.MaxThreads) Task.Delay(100).Wait();
                }

                #endregion

                #region Variables

                var clientIpEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
                var serverIpEndpoint = client.Client.LocalEndPoint as IPEndPoint;

                var clientEndpoint = clientIpEndpoint.ToString();
                var serverEndpoint = serverIpEndpoint.ToString();

                clientIp = clientIpEndpoint.Address.ToString();
                clientPort = clientIpEndpoint.Port;

                var serverIp = serverIpEndpoint.Address.ToString();
                var serverPort = serverIpEndpoint.Port;

                #endregion

                #region Build-HttpRequest

                var req = HttpRequest.FromTcpClient(client);
                if (req == null)
                {
                    _Logging.Warn(clientEndpoint + " unable to build HTTP request");
                    _ActiveThreads--;
                    return;
                }

                req.SourceIp = clientIp;
                req.SourcePort = clientPort;
                req.DestIp = serverIp;
                req.DestPort = serverPort;

                #endregion

                #region Security-Check

                string denyReason = null;
                var isPermitted = _SecurityModule.IsPermitted(req, out denyReason);
                if (!isPermitted) _Logging.Info(clientEndpoint + " request denied by security [" + denyReason + "]");

                #endregion

                #region Process-Connection

                if (req.Method == HttpMethod.CONNECT)
                {
                    _Logging.Debug(clientEndpoint + " proxying request via CONNECT to " + req.FullUrl);
                    ConnectRequest(connectionId, client, req);
                }
                else
                {
                    _Logging.Debug(clientEndpoint + " proxying request to " + req.FullUrl);

                    var resp = ProxyRequest(req).Result;
                    if (resp != null)
                    {
                        var ns = client.GetStream();
                        await SendRestResponse(resp, ns);
                        await ns.FlushAsync();
                        ns.Close();
                    }
                }

                #endregion

                #region Close-Down

                client.Close();
                _ActiveThreads--;

                #endregion
            }
            catch (IOException)
            {
            }
            catch (Exception eInner)
            {
                _Logging.Exception("PuppyProxy", "AcceptConnections", eInner);
            }
        }

        private static async Task<RestResponse> ProxyRequest(HttpRequest request)
        {
            try
            {
                if (request.Headers != null)
                {
                    string foundVal = null;

                    foreach (var currKvp in request.Headers)
                    {
                        if (string.IsNullOrEmpty(currKvp.Key)) continue;
                        if (currKvp.Key.ToLower().Equals("expect"))
                        {
                            foundVal = currKvp.Key;
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(foundVal)) request.Headers.Remove(foundVal);
                }

                var req = new RestRequest(
                    request.FullUrl,
                    (RestWrapper.HttpMethod)Enum.Parse(typeof(RestWrapper.HttpMethod), request.Method.ToString()),
                    request.Headers,
                    request.ContentType);

                if (request.ContentLength > 0)
                    return await req.SendAsync(request.ContentLength, request.Data);
                return await req.SendAsync();
            }
            catch (Exception e)
            {
                _Logging.Exception("PuppyProxy", "ProxyRequest", e);
                return null;
            }
        }

        private static void ConnectRequest(int connectionId, TcpClient client, HttpRequest req)
        {
            Tunnel currTunnel = null;
            TcpClient server = null;

            try
            {
                client.NoDelay = true;
                client.Client.NoDelay = true;

                server = new TcpClient();

                try
                {
                    if (_Settings.OutputProxy.Enabled)
                    {
                        IProxyClient proxyClient = CreateProxy(_Settings.OutputProxy);
                        server = proxyClient.CreateConnection(req.DestHostname, req.DestHostPort);
                    }
                    else
                    {
                        server.Connect(req.DestHostname, req.DestHostPort);
                    }
                }
                catch (Exception e)
                {
                    _Logging.Exception("PuppyProxy", "ConnectRequest", e);
                    _Logging.Debug("ConnectRequest connect failed to " + req.DestHostname + ":" + req.DestHostPort +
                                   (_Settings.OutputProxy.Enabled ? " using output proxy" : " without output proxy"));
                    return;
                }

                server.NoDelay = true;
                server.Client.NoDelay = true;

                var connectResponse = ConnectResponse();
                client.Client.Send(connectResponse);

                currTunnel = new Tunnel(
                    _Logging,
                    req.SourceIp,
                    req.SourcePort,
                    req.DestIp,
                    req.DestPort,
                    req.DestHostname,
                    req.DestHostPort,
                    client,
                    server,
                    _Counter);
                _Tunnels.Add(connectionId, currTunnel);

                while (currTunnel.IsActive()) Task.Delay(100).Wait();
            }
            catch (SocketException)
            {
                // do nothing
            }
            catch (Exception e)
            {
                _Logging.Exception("PuppyProxy", "ConnectRequest", e);
            }
            finally
            {
                _Tunnels.Remove(connectionId);

                if (client != null) client.Dispose();

                if (server != null) server.Dispose();
            }
        }

        private static IProxyClient CreateProxy(OutputProxy outputProxy)
        {
            var toLoggable = new Func<string, string, string, string>((val, ifEmpty, ifSet) =>
                string.IsNullOrEmpty(val) ? ifEmpty : (ifSet ?? val));

            var host = outputProxy.Host;
            var port = outputProxy.Port;
            var user = string.IsNullOrEmpty(outputProxy.User) ? null : outputProxy.User;
            var pass = string.IsNullOrEmpty(outputProxy.Password) ? null : outputProxy.Password;

            _Logging.Debug($"Create proxy {outputProxy.Type} - " +
                           $"Host: {host}," +
                           $" Port: {port}," +
                           $" User: {toLoggable(user, "(empty)", null)}," +
                           $" Password: {toLoggable(pass, "(empty)", "****")}");

            switch (outputProxy.Type)
            {
                case OutputProxy.ProxyType.Socks4:
                {
                    return new Socks4ProxyClient
                    {
                        ProxyHost = host,
                        ProxyPort = port,
                        ProxyUserId = user
                    };
                }
                case OutputProxy.ProxyType.Socks5:
                {
                    return new Socks5ProxyClient()
                    {
                        ProxyHost = host,
                        ProxyPort = port,
                        ProxyUserName = user,
                        ProxyPassword = user
                    };
                }
                default:
                {
                    return !string.IsNullOrEmpty(user)
                        ? new HttpProxyClient(host, port, user, pass)
                        : new HttpProxyClient(host, port);
                }
            }
        }

        private static byte[] ConnectResponse()
        {
            var resp = "HTTP/1.1 200 Connection Established\r\nConnection: close\r\n\r\n";
            return Encoding.UTF8.GetBytes(resp);
        }

        private static async Task SendRestResponse(RestResponse resp, NetworkStream ns)
        {
            try
            {
                byte[] ret = null;
                var statusLine = resp.ProtocolVersion + " " + resp.StatusCode + " " + resp.StatusDescription +
                                 "\r\n";
                ret = Common.AppendBytes(ret, Encoding.UTF8.GetBytes(statusLine));

                if (!string.IsNullOrEmpty(resp.ContentType))
                {
                    var contentTypeLine = "Content-Type: " + resp.ContentType + "\r\n";
                    ret = Common.AppendBytes(ret, Encoding.UTF8.GetBytes(contentTypeLine));
                }

                if (resp.ContentLength > 0)
                {
                    var contentLenLine = "Content-Length: " + resp.ContentLength + "\r\n";
                    ret = Common.AppendBytes(ret, Encoding.UTF8.GetBytes(contentLenLine));
                }

                if (resp.Headers != null && resp.Headers.Count > 0)
                    foreach (var currHeader in resp.Headers)
                    {
                        if (string.IsNullOrEmpty(currHeader.Key)) continue;
                        if (currHeader.Key.ToLower().Trim().Equals("content-type")) continue;
                        if (currHeader.Key.ToLower().Trim().Equals("content-length")) continue;

                        var headerLine = currHeader.Key + ": " + currHeader.Value + "\r\n";
                        ret = Common.AppendBytes(ret, Encoding.UTF8.GetBytes(headerLine));
                    }

                ret = Common.AppendBytes(ret, Encoding.UTF8.GetBytes("\r\n"));

                await ns.WriteAsync(ret, 0, ret.Length);
                await ns.FlushAsync();

                if (resp.Data != null && resp.ContentLength > 0)
                {
                    var bytesRemaining = resp.ContentLength;
                    var buffer = new byte[65536];

                    while (bytesRemaining > 0)
                    {
                        var bytesRead = await resp.Data.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            bytesRemaining -= bytesRead;
                            await ns.WriteAsync(buffer, 0, bytesRead);
                            await ns.FlushAsync();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _Logging.Exception("PuppyProxy", "SendRestResponse", e);
            }
        }

        #endregion
    }
}