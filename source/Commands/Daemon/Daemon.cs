using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ConstrainedExecution;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GrpcDotNetNamedPipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace OpenFrp.Service.Daemon
{
    public class Daemon
    {
        private static Mutex? mutex;

        public static string GetPipename()
        {
            var pipeName = "ofRpcDaemon." + Helpers.HashAlgorithmHelper.ComputeHashToBase64String(Helpers.FileHelper.GetServiceExecutableFile());

            return pipeName;
        }

        internal static Task ExecuteAsync(ParseResult result)
        {
            var pipeName = GetPipename();

            mutex = new Mutex(true, $"service.{pipeName}", out var createdNewFlag);

            if (!createdNewFlag && !mutex.SafeWaitHandle.IsClosed)
            {
                // 已经创建了一个相同命名的进程，取消
                mutex.Close();
                return Task.CompletedTask;
            }

            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                mutex.Close();
            };

            var baseDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

            Directory.CreateDirectory(baseDirectory);

            var host = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

            host.Services.AddLogging(log =>
            {
                log.AddSimpleConsole((x) =>
                {
                    x.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled;
                    x.SingleLine = true;
                });
                log.AddDebug();
                log.AddFile((x) =>
                {
                    x.Files = new Karambolo.Extensions.Logging.File.LogFileOptions[]
                    {
                            new()
                            {
                                Path = $"Daemon-{DateTimeOffset.Now:yyyy.MM.dd}.log",
                            }
                    };
                    x.MaxFileSize = 2048 * 4;
                    x.RootPath = baseDirectory;
                    x.FileAccessMode = Karambolo.Extensions.Logging.File.LogFileAccessMode.KeepOpenAndAutoFlush;
                    x.FileEncoding = Encoding.UTF8;
                });
#if DEBUG
                log.SetMinimumLevel(LogLevel.Trace);
#else
                log.SetMinimumLevel(LogLevel.Information);
#endif
            });
            

            host.Services.AddKeyedSingleton("pipeName",pipeName);
            host.Services.AddSingleton<Manager.Log.LogManager>();
            host.Services.AddSingleton<Manager.Process.ProcessManager>();
            host.Services.AddSingleton<Manager.Frpc.FrpcManager>();
            host.Services.AddSingleton<OpenFrpService>();
            host.Services.AddHostedService<RpcServiceInterface>();

            var app =  host.Build();

       

            return app.RunAsync();
        }

        private class RpcServiceInterface : BackgroundService
        {
            public RpcServiceInterface([FromKeyedServices("pipeName")] string pipeName,ILogger<RpcServiceInterface> logger,OpenFrpService service,
                IHostApplicationLifetime lifetime)
            {
                this.logger = logger;
                this.pipeName = pipeName;
                this.service = service;
                this.lifetime = lifetime;
            }

            private readonly string pipeName;
            private readonly OpenFrpService service;
            private readonly ILogger<RpcServiceInterface> logger;
            private readonly IHostApplicationLifetime lifetime;

            private NamedPipeServer? server;

            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                string serviceName = OpenFrp.Service.WinSrv.ServiceWorker.GetServiceName();

                var services = ServiceController.GetServices();

                foreach (var serve in services)
                {
                    if (!serve.ServiceName.Equals(serviceName))
                    {
                        continue;
                    }
                    logger.LogDebug("Service Display Name: {displayName}, State: {state}", serve.DisplayName, serve.Status);

                    logger.LogWarning("service mode detected.");

                    if (serve.Status is ServiceControllerStatus.Running)
                    {
                        lifetime.StopApplication();
                        return;
                    }
                }

                var cur = WindowsIdentity.GetCurrent();

                if (cur.User is null)
                {
                    lifetime.StopApplication();
                    return;
                }

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
                                lifetime.StopApplication();
                                Environment.Exit(768);
                            }
                            ; return;
                        default:
                            {
                                if (Environment.UserInteractive)
                                {
                                    OpenFrp.Service.Helpers.MessageBoxHelper.MessageBox(IntPtr.Zero, $"{err.Error}", "OPENFRP LAUNCHER DAEMON", (uint)OpenFrp.Service.Helpers.MessageBoxHelper.MessageMode.Confirm);
                                }
                                logger.LogDebug(err.Error,"Unhandle exception");
                            }
                            ; break;
                    }
                };

                Proto.Service.OpenFrp.BindService(server.ServiceBinder, service);

                server.Start();

                logger.LogInformation("service launched!");

                await Task.Delay(-1,stoppingToken).ContinueWith(task =>
                {
                    if (task.IsCanceled)
                    {
                        logger.LogDebug("service shutdown!");

                        server.Kill();
                    }
                });
            }
        }
    }
}
