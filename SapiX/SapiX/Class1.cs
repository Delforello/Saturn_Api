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
using System.Threading.Tasks;
using IWshRuntimeLibrary;


namespace SapiX
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
        private static string LuaScriptNotification;
        private static string LuaScriptUserAgent;
        private static string LuaScriptNameExecutor;
        private static readonly object _injectLock = new object();

        private static readonly Regex KickRegex = new Regex(
            @"(\w+)\s*[:\.]\s*Kick\s*\(([^)]*)\)|" +
            @"(\w+)\s*\[\s*[""']Kick[""']\s*\]\s*\(([^)]*)\)|" +
            @"(\w+)\s*\.\s*Kick\s*\=\s*function|" +
            @"kick\s*\=\s*function",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static string AntiKick(string source)
            => KickRegex.Replace(source, "print('[SapiX Protection] Kick function blocked.')");

        private static void Folders()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string xenoPath = Path.Combine(localAppData, "Xeno");
            string[] directories = {
                Path.Combine(xenoPath, "autoexec"),
                Path.Combine(xenoPath, "scripts"),
                Path.Combine(xenoPath, "workspace")
            };

            foreach (var dir in directories)
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

            string currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            foreach (var dir in directories)
            {
                string shortcut = Path.Combine(currentDir, Path.GetFileName(dir) + ".lnk");
                if (!System.IO.File.Exists(shortcut))
                {
                    var shell = new WshShell();
                    IWshShortcut link = (IWshShortcut)shell.CreateShortcut(shortcut);
                    link.TargetPath = dir;
                    link.WorkingDirectory = dir;
                    link.Description = $"Shortcut to {Path.GetFileName(dir)}";
                    link.Save();
                }
            }
        }

        public static void Inject()
        {
            lock (_injectLock)
            {
                if (_isInjected) return;

                if (!_initialized)
                {
                    Initialize(false);
                    _initialized = true;
                }

                if (GetClientsList().Any(c => c.State == 3))
                {
                    _isInjected = true;
                    return;
                }

                string notifPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Xeno", "autoexec", "CustomNotification.lua");

                Directory.CreateDirectory(Path.GetDirectoryName(notifPath));
                System.IO.File.WriteAllText(notifPath, LuaScriptNotification ?? "");

                Attach();

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 60000)
                {
                    Thread.Sleep(400);
                    if (GetClientsList().Any(c => c.State == 3))
                    {
                        _isInjected = true;
                        Task.Delay(5000).ContinueWith(_ => { try { System.IO.File.Delete(notifPath); } catch { } });
                        return;
                    }
                }

                _isInjected = false;
                Task.Delay(5000).ContinueWith(_ => { try { System.IO.File.Delete(notifPath); } catch { } });
            }
        }

        public static bool IsInjected()
            => _initialized && GetClientsList().Any(c => c.State == 3);

        public static bool IsRobloxOpen()
        {
            Folders();
            return Process.GetProcessesByName("RobloxPlayerBeta").Any();
        }

        public static void KillRoblox()
        {
            foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
                try { p.Kill(); } catch { }
        }

        public static void Execute(string scriptSource)
        {
            var clients = GetClientsList();
            var active = clients.Where(c => c.State == 3).ToList();
            if (active.Count == 0) return;

            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(LuaScriptUserAgent)) sb.AppendLine(LuaScriptUserAgent).AppendLine();
            if (!string.IsNullOrEmpty(LuaScriptNameExecutor)) sb.AppendLine(LuaScriptNameExecutor).AppendLine();

            sb.AppendLine(AntiKick(scriptSource));

            var bytes = Encoding.UTF8.GetBytes(sb.ToString() + "\0");
            Execute(bytes, active.Select(c => c.Id).ToArray(), active.Count);
        }

        public static void SetCustomInjectionNotification(string title = "SapiX", string text = "Injected Successfully!", string idIcon = "", string duration = "5")
        {
            LuaScriptNotification = $@"local coreGui = game:GetService(""CoreGui"")
local robloxGui = coreGui:FindFirstChild(""RobloxGui"")
if robloxGui then
    local endTime = tick() + 3
    while tick() < endTime do
        local nf = robloxGui:FindFirstChild(""NotificationFrame"")
        if nf then for _,c in ipairs(nf:GetChildren()) do if c:IsA(""Frame"") then c:Destroy() end end end
        wait(0.05)
    end
end
game.StarterGui:SetCore(""SendNotification"", {{
    Title = ""{title}"",
    Text = ""{text}"",
    Icon = ""rbxassetid://{idIcon}"",
    Duration = {duration}
}})";
        }

        public static void SetCustomUserAgent(string Name = "SapiX")
        {
            LuaScriptUserAgent = $@"oldr = request
getgenv().request = function(options)
    options.Headers = options.Headers or {{}}
    options.Headers[""User-Agent""] = ""{Name}""
    return oldr(options)
end
request = getgenv().request";
        }

        public static void SetCustomNameExecutor(string Name = "SapiX", string Version = "v1.0.0")
        {
            LuaScriptNameExecutor = $@"local Name, Version = ""{Name}"", ""{Version}""
getgenv().identifyexecutor = function() return Name, Version end
getgenv().getexecutorname = function() return Name end
getgenv().getexploitname = function() return Name end";
        }

        public static List<ClientInfo> GetClientsList()
        {
            IntPtr ptr = GetClients();
            if (ptr == IntPtr.Zero) return new List<ClientInfo>();

            string raw = Marshal.PtrToStringAnsi(ptr);
            if (string.IsNullOrWhiteSpace(raw) || raw == "[]") return new List<ClientInfo>();

            var regex = new Regex(@"^\[?\[?\s*(\d+)\s*,\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*(\d+)", RegexOptions.Multiline);
            var matches = regex.Matches(raw);

            var clients = new List<ClientInfo>();
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, out int id) &&
                    int.TryParse(m.Groups[4].Value, out int state))
                {
                    clients.Add(new ClientInfo
                    {
                        Id = id,
                        Name = m.Groups[2].Value,
                        Version = m.Groups[3].Value,
                        State = state
                    });
                }
            }

            return clients.Any() ? clients : new List<ClientInfo>();
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
