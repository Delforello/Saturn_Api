using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private static readonly object _cacheLock = new object();
        private const int CacheDurationMs = 3000;

        private static readonly Regex ClientRegex = new Regex(
            "\\[\\s*(\\d+),\\s*\"(.*?)\",\\s*\"(.*?)\",\\s*(\\d+)\\s*\\]",
            RegexOptions.Compiled
        );

        private static readonly Regex KickRegex = new Regex(
            @"(\w+)\s*[:\.]\s*Kick\s*\(([^)]*)\)|(\w+)\s*\[\s*[""']Kick[""']\s*\]\s*\(([^)]*)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static void Folders()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string programFilesPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            string[] directories =
            {
        Path.Combine(appDataPath, "Saturn Api", "autoexec"),
        Path.Combine(appDataPath, "Saturn Api", "workspace"),
        Path.Combine(appDataPath, "Saturn Api", "scripts"),
    };

            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            CreateShortcuts(programFilesPath, currentPath);
        }

        private static void CreateShortcuts(string programFilesPath, string targetPath)
        {
            string shortcutFolder = Path.Combine(programFilesPath, "Saturn Api");

            if (!Directory.Exists(shortcutFolder))
            {
                Directory.CreateDirectory(shortcutFolder);
            }

            string shortcutPath = Path.Combine(shortcutFolder, "Saturn Api.lnk");

            var shell = new IWshRuntimeLibrary.WshShell();
            IWshRuntimeLibrary.IWshShortcut shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);

            shortcut.TargetPath = targetPath;
            shortcut.Description = "Collegamento a Saturn Api";
            shortcut.WorkingDirectory = targetPath;
            shortcut.Save();
        }
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
                try { proc.WaitForInputIdle(5000); } catch { }
            }

            Thread.Sleep(500);

            Attach();
            string filePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Xeno", "autoexec", "Agent.lua");

            string directory = Path.GetDirectoryName(filePath);

            if (!File.Exists(filePath)) // MODIFY THIS SCRIPT LUA FOR CUSTOM USER-AGENT
            {
                string luaScript = @"oldr = request

getgenv().request = function(options)
    if options.Headers then
        options.Headers[""User-Agent""] = ""Saturn X""
    else
        options.Headers = {[""User-Agent""] = ""Saturn X""}
    end
    local response = oldr(options)
    return response
end 

request = getgenv().request
";
                File.WriteAllText(filePath, luaScript);
            }
            GetClients();



            RefreshClientsCache();
            if (_cachedClients.Count > 0)
            {
                _isInjected = true;

                return;
            }

            int timeoutMs = 3000;
            int pollInterval = 100;
            int waited = 0;
            while (waited < timeoutMs)
            {
                RefreshClientsCache();
                if (_cachedClients.Count > 0)
                    break;

                Thread.Sleep(pollInterval);
                waited += pollInterval;
            }

            _isInjected = _cachedClients.Count > 0;
        }

        public static bool IsInjected() => _isInjected;

        public static bool IsRobloxOpen()
        {
            Folders();
            return Process.GetProcessesByName("RobloxPlayerBeta").Any();
        }

        public static void KillRoblox()
        {
            foreach (var proc in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                try { proc.Kill(); } catch { }
            }
        }

        private static string AntiKick(string source)
        {
            return KickRegex.Replace(source, "print('[SaturnApi Protection] Kick function blocked.')");
        }

        public static void Execute(string scriptSource)
        {
            var clients = GetClientsList();

            var activeClients = clients.Where(c => c.State == 3).ToList();
            if (activeClients.Count == 0) return;

            string sanitized = AntiKick(scriptSource);

            int[] ids = activeClients.Select(c => c.Id).ToArray();
            byte[] scriptBytes = Encoding.UTF8.GetBytes(sanitized + "\0");
            Execute(scriptBytes, ids, ids.Length);

        }

        public static List<ClientInfo> GetClientsList()
        {
            lock (_cacheLock)
            {
                if ((DateTime.Now - _lastClientFetchTime).TotalMilliseconds < CacheDurationMs)
                    return _cachedClients;

                RefreshClientsCache();
                return _cachedClients;

            }
        }

        private static void RefreshClientsCache()
        {
            IntPtr ptr = GetClients();
            if (ptr == IntPtr.Zero) return;

            string raw = Marshal.PtrToStringAnsi(ptr);
            if (string.IsNullOrWhiteSpace(raw)) return;

            var freshClients = new List<ClientInfo>();
            var matches = ClientRegex.Matches(raw);
            foreach (Match match in matches)
            {
                freshClients.Add(new ClientInfo
                {
                    Id = int.Parse(match.Groups[1].Value),
                    Name = match.Groups[2].Value,
                    Version = match.Groups[3].Value,
                    State = int.Parse(match.Groups[4].Value)
                });
            }

            _cachedClients = freshClients;
            _lastClientFetchTime = DateTime.Now;
        }

        public struct ClientInfo
        {
            public int Id;
            public string Name;
            public string Version;
            public int State;

        }

    }
}