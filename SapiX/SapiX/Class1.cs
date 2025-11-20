using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IWshRuntimeLibrary;
using File = System.IO.File;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static bool _initialized = false;
        private static bool _isInjected;
        private static string LuaScriptNotification;
        private static string LuaScriptUserAgent;
        private static string LuaScriptNameExecutor;
        private static readonly object _injectLock = new object();

        private static readonly Regex KickRegex = new Regex(
            @"(\w+)\s*[:\.]\s*Kick\s*\(|(\w+)\s*\[\s*[""']Kick[""']\s*\]\s*\(|(\w+)\s*\.\s*Kick\s*=\s*function|kick\s*=\s*function",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string AntiKick(string source) =>
            KickRegex.Replace(source, "print('[SapiX Protection] Kick function blocked.')");


        private const int SW_HIDE = 0;

        private static void Folders()
        {
            string xenoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Xeno");
            string[] dirs = { "autoexec", "scripts", "workspace" };
            foreach (string d in dirs)
            {
                string path = Path.Combine(xenoPath, d);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            foreach (string d in dirs)
            {
                string lnk = Path.Combine(exeDir, d + ".lnk");
                if (!System.IO.File.Exists(lnk))
                {
                    var shell = new WshShell();
                    IWshShortcut link = (IWshShortcut)shell.CreateShortcut(lnk);
                    link.TargetPath = Path.Combine(xenoPath, d);
                    link.WorkingDirectory = link.TargetPath;
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
                    if (AllocConsole())
                    {
                        IntPtr consoleWindow = GetConsoleWindow();

                        if (consoleWindow != IntPtr.Zero)
                        {
                            ShowWindow(consoleWindow, SW_HIDE);
                        }
                    }
                    else
                    {
                        AttachConsole(ATTACH_PARENT_PROCESS);
                    }

                    Initialize(true);

                    FreeConsole();

                    _initialized = true;
                }

                string notifPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Xeno", "autoexec", "CustomNotification.lua");

                Directory.CreateDirectory(Path.GetDirectoryName(notifPath));
                File.WriteAllText(notifPath, LuaScriptNotification ?? "");

                Attach();

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 80000)
                {
                    Thread.Sleep(500);
                    var clients = GetClientsList();
                    if (clients.Count > 0 && clients[0].State == 3)
                    {
                        _isInjected = true;
                        Task.Delay(5000).ContinueWith(_ => { try { File.Delete(notifPath); } catch { } });
                        return;
                    }
                }

                _isInjected = false;
                Task.Delay(5000).ContinueWith(_ => { try { File.Delete(notifPath); } catch { } });
            }
        }

        public static bool IsInjected()
            => _initialized && GetClientsList().Count > 0 && GetClientsList()[0].State == 3;

        public static bool IsRobloxOpen()
        {
            Folders();
            return Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;
        }

        public static void KillRoblox()
        {
            foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
                try { p.Kill(); } catch { }
        }

        public static void Execute(string scriptSource)
        {
            var clients = GetClientsList();
            if (clients.Count == 0 || clients[0].State != 3) return;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(LuaScriptUserAgent)) sb.AppendLine(LuaScriptUserAgent).AppendLine();
            if (!string.IsNullOrEmpty(LuaScriptNameExecutor)) sb.AppendLine(LuaScriptNameExecutor).AppendLine();
            sb.AppendLine(AntiKick(scriptSource));

            var bytes = Encoding.UTF8.GetBytes(sb.ToString() + "\0");
            Execute(bytes, new int[] { clients[0].Id }, 1);
        }

        public static void SetCustomInjectionNotification(string title = "SapiX", string text = "Injected Successfully!", string idIcon = "", string duration = "5")
        {
            LuaScriptNotification = $@"local coreGui = game:GetService(""CoreGui"")
local rg = coreGui:FindFirstChild(""RobloxGui"")
if rg then
    task.spawn(function()
        wait(3)
        for _,v in ipairs(rg.NotificationFrame:GetChildren()) do
            if v:IsA(""Frame"") then v:Destroy() end
        end
    end)
end
game.StarterGui:SetCore(""SendNotification"", {{
    Title = ""{title}"",
    Text = ""{text}"",
    Icon = ""rbxassetid://{idIcon}"",
    Duration = {duration}
}})";
        }

        public static void SetCustomUserAgent(string name = "SapiX")
            => LuaScriptUserAgent = $@"oldr=request getgenv().request=function(o) o.Headers=o.Headers or {{}} o.Headers[""User-Agent""]=""{name}"" return oldr(o) end request=getgenv().request";

        public static void SetCustomNameExecutor(string name = "SapiX", string ver = "v1.0.0")
            => LuaScriptNameExecutor = $@"getgenv().identifyexecutor=function()return""{name}"",""{ver}""end getgenv().getexecutorname=function()return""{name}""end getgenv().getexploitname=function()return""{name}""end";

        public static List<ClientInfo> GetClientsList()
        {
            IntPtr ptr = GetClients();
            if (ptr == IntPtr.Zero) return new List<ClientInfo>();

            string raw = Marshal.PtrToStringAnsi(ptr);
            if (string.IsNullOrWhiteSpace(raw) || raw == "[]") return new List<ClientInfo>();

            var regex = new Regex(@"\[?\s*\[?\s*(\d+)\s*,\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*(\d+)", RegexOptions.Multiline);
            var matches = regex.Matches(raw);

            var list = new List<ClientInfo>();
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, out int id) && int.TryParse(m.Groups[4].Value, out int state))
                {
                    list.Add(new ClientInfo
                    {
                        Id = id,
                        Name = m.Groups[2].Value,
                        Version = m.Groups[3].Value,
                        State = state
                    });
                }
            }
            return list;
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
