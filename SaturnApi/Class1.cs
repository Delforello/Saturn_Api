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
using IWshRuntimeLibrary;

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
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string xenoPath = Path.Combine(localAppData, "Xeno");

            string[] directories =
            {
                Path.Combine(xenoPath, "autoexec"),
                Path.Combine(xenoPath, "scripts"),
                Path.Combine(xenoPath, "workspace")
            };

            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    Console.WriteLine($"Creata cartella: {dir}");
                }
            }

            string currentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            foreach (var sourceDir in directories)
            {
                string folderName = Path.GetFileName(sourceDir);
                string shortcutPath = Path.Combine(currentDirectory, $"{folderName}.lnk");

                if (!System.IO.File.Exists(shortcutPath))
                {
                    var shell = new WshShell();
                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);

                    shortcut.TargetPath = sourceDir;
                    shortcut.Description = $"Collegamento a {folderName}";
                    shortcut.WorkingDirectory = sourceDir;
                    shortcut.Save();
                }
            }
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

            string directory = Path.GetDirectoryName(filePath); // MODIFY THIS LUA SCRIPT FOR CUSTOM USER-AGENT
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

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            System.IO.File.WriteAllText(filePath, luaScript);

            int timeoutMs = 3000;
            int pollInterval = 100;
            int waited = 0;

            while (waited < timeoutMs)
            {
                var clients = GetClientsList();
                if (clients.Count > 0)
                {
                    _isInjected = true;
                    return;
                }

                Thread.Sleep(pollInterval);
                waited += pollInterval;
            }

            _isInjected = false;
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
            IntPtr ptr = GetClients();
            if (ptr == IntPtr.Zero) return new List<ClientInfo>();

            string raw = Marshal.PtrToStringAnsi(ptr);
            if (string.IsNullOrWhiteSpace(raw)) return new List<ClientInfo>();

            var clients = new List<ClientInfo>();
            var matches = ClientRegex.Matches(raw);
            foreach (Match match in matches)
            {
                clients.Add(new ClientInfo
                {
                    Id = int.Parse(match.Groups[1].Value),
                    Name = match.Groups[2].Value,
                    Version = match.Groups[3].Value,
                    State = int.Parse(match.Groups[4].Value)
                });
            }

            return clients;
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
