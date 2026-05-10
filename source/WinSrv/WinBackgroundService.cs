using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using GrpcDotNetNamedPipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFrp.Service.Daemon;
using OpenFrp.Service.Manager.Frpc;

namespace OpenFrp.Service.WinSrv
{
    internal class WindowsBackgroundService : BackgroundService
    {
        public WindowsBackgroundService(ServiceWorker worker,
            OpenFrpService openFrpService,
            Manager.Frpc.FrpcManager frpcManager,
            Manager.Process.ProcessManager processManager,
            ILogger<WindowsBackgroundService> logger)
        {
            this.worker = worker;
            this.logger = logger;

            this.openFrpService = openFrpService;
            this.processManager = processManager;
            this.frpcManager = frpcManager;

            this.jsonSerializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All)
            };
        }

        private readonly JsonSerializerOptions jsonSerializerOptions;
        private readonly string currentServiceName = ServiceWorker.GetServiceName();

        private readonly Manager.Process.ProcessManager processManager;
        private readonly Manager.Frpc.FrpcManager frpcManager;
        private readonly OpenFrpService openFrpService;
        private readonly ILogger<WindowsBackgroundService> logger;
        private readonly ServiceWorker worker;

        private NamedPipeServer? server;
        private ServiceConfig? config;
        private System.Timers.Timer? timer;

        private byte ActiveCheckUpdateCount { get; set; } = 0;
        private Task? CurrentLoginTask { get; set; }

        private string? CurrentUserHashCode { get; set; }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (config is null)
            {
                config ??= new ServiceConfig { };
            }
            config.UserAuthorization = Net.OpenFrpApi.GetAuthorization();
            config.UserAutoLaunchTunnel = processManager.GetOnlineProcesses();

            processManager?.Shutdown();
            server?.Kill();
            timer?.Stop();

            try
            {
                if (!Helpers.FileHelper.TryGetServiceConfigFile(currentServiceName, out string path))
                {
                    logger.LogError("[StopAsync] 无法找到服务配置文件。({reason})", path);
                    return;
                }

                FileStream? fs = default;
                try
                {
                    if (File.GetAttributes(path) is FileAttributes.Hidden)
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                    }
                    if ((fs = File.Open(path, FileMode.Create)) is null)
                    {
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法打开服务配置文件流。");
                    }
                    
                    fs.Seek(0, SeekOrigin.Begin);
                    fs.SetLength(0); // 清空文件内容

                    await JsonSerializer.SerializeAsync(fs, config, options: jsonSerializerOptions, cancellationToken: cancellationToken);

                    await fs.FlushAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "无法写入服务配置文件。");
                }
                finally
                {
                    fs?.Close();
                }
                
            }
            finally
            {
                await base.StopAsync(cancellationToken);
            }
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!Helpers.FileHelper.TryGetServiceConfigFile(currentServiceName, out string path))
                {
                    logger.LogError("[StartAsync] 无法找到服务配置文件。({reason})", path);
                    return;
                }
                if (!File.Exists(path))
                {
                    logger.LogWarning("服务配置文件不存在，创建默认配置文件。");

                    config = new ServiceConfig { };
                }
                else
                {
                    FileStream? fs = default;
                    try
                    {
                        if ((fs = File.OpenRead(path)) is null)
                        {
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法打开服务配置文件。");
                        }
                        fs.Seek(0, SeekOrigin.Begin);

                        config = await System.Text.Json.JsonSerializer.DeserializeAsync<ServiceConfig>(fs, cancellationToken: cancellationToken);

                        if (config is not null)
                        {
                            logger.LogInformation("服务配置文件加载成功。");
                            //logger.LogDebug("Current Property: {property}", config.TestProperty);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "无法打开服务配置文件,或读取配置文件失败。");
                    }
                    finally
                    {
                        fs?.Close();
                    }
                }
            }
            finally
            {

                await base.StartAsync(cancellationToken);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            config ??= new ServiceConfig();

            openFrpService.SyncSettingUpdate = setting =>
            {
                config.UseDebug = setting.UseDebug;
                config.UseDoh = setting.UseDoh;
                config.UseForceTlsEncrypt = setting.UseForceTls;
            };

            if (Environment.OSVersion.Version is { Major: 6, Minor: < 2 })
            {
                // Windows 7 RTM: 6.1.7601
                // Fallback to config mode
                config.UseTomlConfig = true;
            }

            string pipeName = Daemon.Daemon.GetPipename(); ;

            var cur = WindowsIdentity.GetCurrent();

            if (cur.User is null) return;

            var security = new PipeSecurity();

            security.AddAccessRule(new PipeAccessRule(cur.User, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

            server = new NamedPipeServer(pipeName, new NamedPipeServerOptions()
            {
                PipeSecurity = security
            });
            server.Error += (_, err) =>
            {
                switch (err.Error)
                {
                    case ObjectDisposedException: return;
                    case UnauthorizedAccessException:
                        {
                            Environment.Exit(768);
                        }
                        ; return;
                    default:
                        {
                            logger.LogError(err.Error, "Unhandle Exception");
                        }
                        ; break;
                }
            };
            Proto.Service.OpenFrp.BindService(server.ServiceBinder, openFrpService);

            server.Start();

            logger.LogDebug("service launched!");

            timer = new System.Timers.Timer
            {
                AutoReset = true,
                Interval = 1000 * 30, // 30 seconds
            };
            timer.Elapsed += OnTimerElapsed;
            timer.Start();

            if (!string.IsNullOrEmpty(config.UserAuthorization))
            {
                try
                {
                    if (await frpcManager.DetectFrpcVersionAndFeatrue())
                    {
                        CurrentLoginTask ??= LoginUserService(config.UserAuthorization!, stoppingToken);
                    }
                }
                catch(Exception ex)
                {
                    logger.LogError(ex, "无法获取 FRPC 版本，请确认 FRPC 可执行文件是否被杀软拦截或删除。");
                }
            }

            await Task.Delay(-1, stoppingToken);
        }

        private void OnTimerElapsed(object? sender,System.Timers.ElapsedEventArgs e)
        {
            ActiveCheckUpdateCount += 1;

            if (ActiveCheckUpdateCount >= 60)
            {
                ActiveCheckUpdateCount = 0;
            }

            if (openFrpService.HasUser() && config is { UserAuthorization: string auth } && !string.IsNullOrEmpty(auth))
            {
                CurrentLoginTask ??= LoginUserService(auth);
            }
        }
        
        private async Task LoginUserService(string userAuth,CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userAuth)) return;

            bool alreadyRetryWhenTunnelFailedToGet = false;

            baseStartupBlock:

            Net.OpenFrpApi.SetAuthorization(userAuth);

            Yue3.Model.OpenFrp.Response.Data.UserInfo? userInfo = default;

            for (int i = 0; i < 5; i++)
            {
                var resp = await Net.OpenFrpApi.GetUserInfo(cancellationToken);

                if (resp.StatusCode is not System.Net.HttpStatusCode.OK || resp.Data is null || string.IsNullOrEmpty(resp.Data.UserToken))
                {
                    logger.LogError(resp.Exception, "[LoginUserService] 无法获取用户信息，尝试重新登录。({statusCode})", resp.StatusCode);

                    await Task.Delay(1000 * 5, cancellationToken);

                    continue;
                }

                string? hashCode = openFrpService.SetUser(userInfo = resp.Data);

                if (hashCode is null)
                {
                    logger.LogWarning("[LoginUserService] 当前已有其他账户登录，取消登录；");
                    return;
                }
                CurrentUserHashCode = hashCode;
                break;
            }
            if (string.IsNullOrEmpty(CurrentUserHashCode) || userInfo is null)
            {
                logger.LogWarning("[LoginUserService] 无法获取用户信息，登录失败。将在 30s 后自动重试。");
                return;
            }
            if (config?.UserAutoLaunchTunnel is not { Length: > 0 } autoLaunchTunnels)
            {
                return;
            }
#if NET
            logger.LogDebug("[LoginUserService] 自动启动隧道：{tunnels}", string.Join(',', autoLaunchTunnels));
#else
            logger.LogDebug("[LoginUserService] 自动启动隧道：{tunnels}", string.Join(",", autoLaunchTunnels));
#endif
            var rev = await Net.OpenFrpApi.GetUserTunnels(cancellationToken);

            if (rev.StatusCode is not System.Net.HttpStatusCode.OK || rev.Data is null || rev.Data.List is null)
            {
                logger.LogError(rev.Exception, "[LoginUserService] 无法获取用户隧道列表。({statusCode})", rev.StatusCode);

                if (alreadyRetryWhenTunnelFailedToGet)
                {
                    return;
                }
                alreadyRetryWhenTunnelFailedToGet = true;

                goto baseStartupBlock;
            }
            if (rev.Data.Total is 0)
            {
                logger.LogWarning("[LoginUserService] 获取用户隧道数为零，已将配置中自启动隧道设置为空。");
                config.UserAutoLaunchTunnel = Array.Empty<int>();
                return;
            }

            var requestTunnels = new HashSet<Yue3.Model.OpenFrp.Response.Data.UserTunnel>();

            var confiVg = new Manager.Process.LaunchConfig
            {
                IsAutoLuanch = true,
                UseDebug = config.UseDebug,
                ForceTlsEncrypt = config.UseForceTlsEncrypt,
                DisableColorConsole = frpcManager.Feature.AllowDisableConsoleColor,
            };

            var useConfig = frpcManager.Feature.ForceUseConfig || config.UseTomlConfig;

            foreach (var tun in rev.Data.List)
            {
                if (autoLaunchTunnels.Contains(tun.Id))
                {
                    if (useConfig && !string.IsNullOrEmpty(tun.Name))
                    {
                        requestTunnels.Add(tun);
                    }
                    else
                    {
                        logger.LogDebug("[LoginAutoService] AutoLaunch: #{id} {name}", tun.Id,tun.Name);

                        var r = processManager.LaunchTunnel(userInfo.UserToken!, tun, confiVg);
                        // post to launch
                    }
                }
            }

            if (!useConfig)
            {
                return;
            }
            IEnumerable<string> nameCollection = requestTunnels.Select(x => x.Name!);

            foreach (var nid in requestTunnels.Select(x => x.NodeId).Distinct())
            {
                var nodeConf = await OpenFrp.Service.Net.OpenFrpApi.GetNodeConfig(nid, cancellationToken);

                if (nodeConf.StatusCode is not System.Net.HttpStatusCode.OK || string.IsNullOrEmpty(nodeConf.Data))
                {
                    continue;
                }

                try
                {
                    var table = Tomlyn.TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(nodeConf.Data!);

                    Tomlyn.Model.TomlTable[] sourceArray;

                    if (table != null && table.TryGetValue("proxies", out var val) && val is Tomlyn.Model.TomlTableArray proxies)
                    {
                        sourceArray = new Tomlyn.Model.TomlTable[proxies.Count];

                        proxies.CopyTo(sourceArray, 0);
                    }
                    else
                    {
                        continue;
                    }

                    if (sourceArray.Length is 0) continue;

                    table.Remove("proxies");

                    foreach (var tunV2 in sourceArray)
                    {
                        if (tunV2.TryGetValue("name", out var name) && name is not null or "" && nameCollection.Contains(name))
                        {
                            table.Add("proxies", new Tomlyn.Model.TomlTable[1] { tunV2 });

                            if (requestTunnels.FirstOrDefault(x => x.Name!.Equals(name)) is { } tf)
                            {
                                logger.LogDebug("[LoginAutoService] AutoLaunch: #{id} {name}", tf.Id, tf.Name);
                                var r = processManager.LaunchTunnel(userInfo.UserToken!, Tomlyn.TomlSerializer.Serialize(table), tf, confiVg);
                            }
                        }
                        table.Remove("proxies");
                    }
                }
                catch
                {
                    continue;
                }
            }
        }
    }
}
