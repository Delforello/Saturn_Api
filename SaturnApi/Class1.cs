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
        private static string LuaScriptNotification;
        private static string LuaScriptUserAgent;
        private static string LuaScriptNameExecutor;

        private static readonly Regex ClientRegex = new Regex(
            "\\[\\s*(\\d+),\\s*\"(.*?)\",\\s*\"(.*?)\",\\s*(\\d+)\\s*\\]",
            RegexOptions.Compiled
        );

        private static readonly Regex KickRegex = new Regex(
            @"(\w+)\s*[:\.]\s*Kick\s*\(([^)]*)\)|" +
            @"(\w+)\s*\[\s*[""']Kick[""']\s*\]\s*\(([^)]*)\)|" +
            @"(\w+)\s*\.\s*Kick\s*\=\s*function|" +
            @"kick\s*\=\s*function",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        private static string AntiKick(string source)
        {
            return KickRegex.Replace(source, "print('[SaturnApi Protection] Kick function blocked.')");
        }
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
                    shortcut.Description = $"Linked {folderName}";
                    shortcut.WorkingDirectory = sourceDir;
                    shortcut.Save();
                }
            }
        }

        public static void Inject()
        {
            int timeoutMs = 3000;
            int pollInterval = 100;
            int waited = 0;

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

            string filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Xeno", "autoexec", "CustomNotification.lua");

            string directory = Path.GetDirectoryName(filePath);

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            System.IO.File.WriteAllText(filePath, LuaScriptNotification);

            Thread.Sleep(500);
            Attach();

            while (waited < timeoutMs)
            {
                var clients = GetClientsList();
                if (clients.Count > 0)
                {
                    _isInjected = true;

                    Task.Delay(5000).ContinueWith(_ =>
                    {
                        try
                        {
                            if (System.IO.File.Exists(filePath))
                            {
                                System.IO.File.Delete(filePath);
                            }
                        }
                        catch { }
                    });
                    return;
                }

                Thread.Sleep(pollInterval);
                waited += pollInterval;
            }

            Task.Delay(5000).ContinueWith(_ =>
            {
                try
                {
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }
                catch { }
            });

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

        public static void Execute(string scriptSource)
        {
            var clients = GetClientsList();
            var activeClients = clients.Where(c => c.State == 3).ToList();
            if (activeClients.Count == 0) return;

            StringBuilder combinedScript = new StringBuilder();

            if (!string.IsNullOrEmpty(LuaScriptUserAgent))
            {
                combinedScript.AppendLine(LuaScriptUserAgent);
                combinedScript.AppendLine();
            }

            if (!string.IsNullOrEmpty(LuaScriptNameExecutor))
            {
                combinedScript.AppendLine(LuaScriptNameExecutor);
                combinedScript.AppendLine();
            }

            string sanitized = AntiKick(scriptSource);
            combinedScript.AppendLine(sanitized);

            int[] ids = activeClients.Select(c => c.Id).ToArray();
            byte[] scriptBytes = Encoding.UTF8.GetBytes(combinedScript.ToString() + "\0");
            Execute(scriptBytes, ids, ids.Length);
        }

        public static void SetCustomInjectionNotification(string title, string text, string idIcon, string duration)
        {
            if (string.IsNullOrEmpty(title)) title = "Saturn Api";
            if (string.IsNullOrEmpty(text)) text = "Injected Successfully!";
            if (string.IsNullOrEmpty(idIcon)) idIcon = "";
            if (string.IsNullOrEmpty(duration)) duration = "5";

            LuaScriptNotification = $@"local coreGui = game:GetService(""CoreGui"")
            local robloxGui = coreGui:FindFirstChild(""RobloxGui"")
            if robloxGui then
                local endTime = tick() + 3
                while tick() < endTime do
                    local notificationFrame = robloxGui:FindFirstChild(""NotificationFrame"")
                    if notificationFrame then
                        for _, child in ipairs(notificationFrame:GetChildren()) do
                            if child:IsA(""Frame"") then
                                child:Destroy()
                            end
                        end
                    end
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

        public static void SetCustomUserAgent(string Name)
        {
            if (string.IsNullOrEmpty(Name)) Name = "Saturn Api";

            LuaScriptUserAgent = $@"oldr = request
            getgenv().request = function(options)
                if options.Headers then
                    options.Headers[""User-Agent""] = ""{Name}""
                else
                    options.Headers = {{[""User-Agent""] = ""{Name}""}}
                end
                local response = oldr(options)
                return response
            end 
            request = getgenv().request";
        }

        public static void SetCustomNameExecutor(string Name, string Version)
        {
            if (string.IsNullOrEmpty(Name)) Name = "Saturn Api";
            if (string.IsNullOrEmpty(Version)) Version = "v1.0.0";

            LuaScriptNameExecutor = $@"local Name = ""{Name}""
            local Version = ""{Version}""
            getgenv().identifyexecutor = function()
                return Name, Version
            end
            getgenv().getexecutorname = function()
                return Name
            end
            getgenv().getexploitname = function()
                return Name
            end";
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

