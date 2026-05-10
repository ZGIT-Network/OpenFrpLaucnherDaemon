using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LogLevel = OpenFrp.Service.Proto.Response.LogStreamResponse.Types.LogContainer.Types.LogLevel;

namespace OpenFrp.Service.Manager.Process
{
    public partial class ProcessContainer
    {
        public ProcessContainer(
            ProcessManager manager,
            Log.LogManager logManager,
            System.Diagnostics.Process process, 
            Yue3.Model.OpenFrp.Response.Data.UserTunnel tunnel,
            LaunchConfig config,
            ILogger<ProcessContainer> logger)
        {
            this.Process = process;

            //process.ErrorDataReceived += this.StdError;
            //process.OutputDataReceived += this.StdOutput;

            process.Exited += this.Exited;

            this.manager = manager;
            this.logger = logger;
            this.UserTunnel = tunnel;
            this.logManager = logManager;
            this.config = config;

            try
            {
                // update log tag
                if (logManager.Logs.ContainsKey(tunnel.Id) && !string.IsNullOrEmpty(tunnel.Name))
                {
                    logManager.Logs[tunnel.Id].Tag = $"Tunnel/{tunnel.Name}";
                }
            }
            catch
            {
                    
            }

            StartTime = DateTimeOffset.UtcNow;
        }

        private readonly ProcessManager manager;
        private readonly Log.LogManager logManager;
        private readonly ILogger<ProcessContainer> logger;
        private bool isDisposed = false;
        private readonly LaunchConfig config;

#if NET
        [GeneratedRegex(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}.\d{0,3} ")]
        private static partial Regex GetTimestampRegex();
#elif NETFRAMEWORK
        private static Regex GetTimestampRegex() => new(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}.\d{0,3} ");
#endif

        private static readonly Regex TimeStampRegex = GetTimestampRegex();

        public int RestartCount { get; private set; }

        public bool IsFastLaunch { get => config.IsFastLaunch; }
        public bool IsAutoLuanch { get => config.IsAutoLuanch; }

        public DateTimeOffset StartTime { get; private set; }

        public Yue3.Model.OpenFrp.Response.Data.UserTunnel UserTunnel { get; private set; }

        public System.Diagnostics.Process Process { get; private set; }

        public void StdOutput(object? sender, DataReceivedEventArgs arg)
        {
            logger.LogDebug("Test--------------------------- {t}", arg.Data);
            if (string.IsNullOrWhiteSpace(arg.Data))
            {
                return;
            }
            StdOutput(sender, arg.Data);
        }
        public void StdOutput(object? sender, string data)
        {


            

            string ed = TimeStampRegex.Replace(data.TrimEnd(), string.Empty);
            LogLevel fLevel = LogLevel.Info;
            if(sender is null)
            {
                fLevel = LogLevel.Error;
            }
            else switch (ed)
            {
                case string when ed.Contains("[E]"):
                    {
                        fLevel = LogLevel.Error; break;
                    };
                case string when ed.Contains("[I]"):
                    {
                        fLevel = LogLevel.Info; break;
                    };
                case string when ed.Contains("[W]"):
                    {
                        fLevel = LogLevel.Warning; break;
                    };
                case string when ed.Contains("[D]"):
                    {
                        fLevel = LogLevel.Debug; break;
                    };
            }

            logger.LogDebug("[{tunnelName}] {data}", UserTunnel.Name, data);

            logManager.WriteLog(UserTunnel.Id, $"Tunnel/{UserTunnel.Name}", ed, fLevel);
        }

        public void StdError(object? sender, DataReceivedEventArgs arg)
        {
            StdOutput(default, arg);

            if (arg.Data is null) return;

            logger.LogWarning("[{tunnelName}] {data}", UserTunnel.Name, arg.Data);
        }

        public async void Exited(object? sender, EventArgs arg)
        {
            logger.LogInformation("[{tunnelName}] exited", UserTunnel.Name);
            if (isDisposed)
            {
                return;
            }
            RestartCount++;
            if (RestartCount > 5)
            {
                manager.HandleError(new IOException($"隧道 {UserTunnel.Name} 多次重启仍无法启动，已自动关闭。"));
                manager.CloseTunnel(UserTunnel);
                
                return;
            }
            logManager.WriteLog(UserTunnel.Id, $"Tunnel/{UserTunnel.Name}", $"FRPC 进程已退出 (Code: {Process.ExitCode} , 重启计数: {RestartCount} / 5) ", LogLevel.Warning);

            logger.LogWarning("[{tunnelName}] Restart: {index}/5", UserTunnel.Name, RestartCount);

            try { Process.CancelOutputRead(); } catch { }
            try { Process.CancelErrorRead(); } catch { }

            if (DateTimeOffset.UtcNow - StartTime is { TotalSeconds: < 5 })
            {
                logManager.WriteLog(UserTunnel.Id, $"Tunnel/{UserTunnel.Name}", $"进程在极短的时间内重启，是否已链接网络？等待 {RestartCount * 1000} ms 后重启。", LogLevel.Warning);
            }

            await Task.Delay(RestartCount * 1000);

            if (!manager.ContainKey(UserTunnel.Id)) return;

            try
            {
                if (Process.Start())
                {
                    Process.BeginErrorReadLine();
                    Process.BeginOutputReadLine();

                    StartTime = DateTimeOffset.UtcNow;
                }
            }
            catch(Exception wex)
            {
                if (manager.ContainKey(UserTunnel.Id))
                {
                    logger.LogWarning(wex, "Tunnel {tunnelName} restart failed", UserTunnel.Name);
                    logManager.WriteLog(UserTunnel.Id, $"Tunnel/{UserTunnel.Name}", $"进程重启时发生了错误: {wex}", LogLevel.Error);
                    manager.HandleError(wex);
                    manager.CloseTunnel(UserTunnel);
                }
            }
        }

        public void Close()
        {
            
            if (Process.HasExited)
            {
                return;
            }

            Process.Exited -= Exited;

            try { Process.Kill(); } catch { }

            //Process.CancelOutputRead();
            //Process.CancelErrorRead();

            Process.Dispose();

            isDisposed = true;

        }
    }
}
