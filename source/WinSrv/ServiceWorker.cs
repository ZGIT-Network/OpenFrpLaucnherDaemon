using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using GrpcDotNetNamedPipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using OpenFrp.Service.Daemon;
using static OpenFrp.Service.Win32Native;

namespace OpenFrp.Service.WinSrv
{
    public class ServiceWorker
    {        
        public ServiceWorker(ILogger<ServiceWorker> logger)
        {
            this.logger = logger;
        }

        private readonly ILogger<ServiceWorker> logger;

        public static string GetServiceName()
        {
            return $"OpenFrpService_{OpenFrp.Service.Daemon.Daemon.GetPipename().Substring(12, 5)}";
        }

        public static string GetServiceDisplayName()
        {
            return $"OpenFrp 桌面端服务 [{OpenFrp.Service.Daemon.Daemon.GetPipename().Substring(12, 5)}]";
        }

        internal static async Task ExecuteAsync()
        {
            var baseDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

            Directory.CreateDirectory(baseDirectory);

            HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = GetServiceName();
            });

            builder.Services.AddLogging(option =>
            {
                option.AddSimpleConsole((x) =>
                {
                    x.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled;
                    x.SingleLine = true;
                });
                option.AddDebug();
                option.AddFile((x) =>
                {
                    x.Files = new Karambolo.Extensions.Logging.File.LogFileOptions[]
                    {
                        new()
                        {
                            Path = $"WinService-{DateTimeOffset.Now:yyyy.MM.dd}.log",
                        }
                    };
                    x.MaxFileSize = 2048 * 4;
                    x.RootPath = baseDirectory;
                    x.FileAccessMode = Karambolo.Extensions.Logging.File.LogFileAccessMode.KeepOpenAndAutoFlush;
                    x.FileEncoding = Encoding.UTF8;
                });
                option.SetMinimumLevel(LogLevel.Trace);
            });
            builder.Services.AddSingleton<ServiceWorker>();

            builder.Services.AddSingleton<Manager.Process.ProcessManager>();
            builder.Services.AddSingleton<Manager.Log.LogManager>();
            builder.Services.AddSingleton<Manager.Frpc.FrpcManager>();
            builder.Services.AddSingleton<OpenFrpService>();


            builder.Services.AddHostedService<WindowsBackgroundService>();

            IHost host = builder.Build();
            
            await host.RunAsync();
        }

        internal static nint LaunchService(nint hService)
        {
            var proc = new Win32Native.SERVICE_STATUS_PROCESS();
            uint bufferSize = (uint)Marshal.SizeOf<Win32Native.SERVICE_STATUS_PROCESS>();
            if (Win32Native.QueryServiceStatusEx(hService, 0, ref proc, bufferSize, out _))
            {
                if (proc.dwCurrentState is not Win32Native.ServiceState.SERVICE_RUNNING or Win32Native.ServiceState.SERVICE_START_PENDING)
                {
                    if (Win32Native.StartService(hService, 0, null!))
                    {
                        return 0;
                    }
                }
            }
           
            return Marshal.GetLastWin32Error();
        }

        internal static nint KillService(nint hService)
        {
            var proc = new Win32Native.SERVICE_STATUS_PROCESS();
            uint bufferSize = (uint)Marshal.SizeOf<Win32Native.SERVICE_STATUS_PROCESS>();
            if (Win32Native.QueryServiceStatusEx(hService, 0, ref proc, bufferSize, out _))
            {
                if (proc.dwCurrentState is Win32Native.ServiceState.SERVICE_RUNNING)
                {
                    var pro = new Win32Native.SERVICE_STATUS_PROCESS { };
                    if (Win32Native.ControlService(hService, Win32Native.ServiceControlType.SERVICE_CONTROL_STOP, ref pro))
                    {
                        
                        return 0;
                    }
                }
                return Marshal.GetLastWin32Error();
            }
            return Marshal.GetLastWin32Error();
        }

        internal static async Task LaunchService()
        {
            if (!IsAdministrator())
            {
                return;
            }

            var serviceName = GetServiceName();
            var services = ServiceController.GetServices();

            foreach (var serve in services)
            {
                if (!serve.ServiceName.Equals(serviceName))
                {
                    continue;
                }
                if (serve.Status is not ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                {
                    try
                    {
                        serve.Start();

                        return;
                    }
                    catch
                    {

                    }
                }
            }

            try
            {
                await Task.Run(() =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"start \"{WinSrv.ServiceWorker.GetServiceName()}\"",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                });
            }
            catch
            {

            }
        }

        internal static async Task KillService()
        {
            if (!IsAdministrator())
            {
                return;
            }

            var serviceName = GetServiceName();
            var services = ServiceController.GetServices();

            foreach (var serve in services)
            {
                if (!serve.ServiceName.Equals(serviceName))
                {
                    continue;
                }
                if (serve.Status is ServiceControllerStatus.Running)
                {
                    try
                    {
                        serve.Stop();

                        return;
                    }
                    catch
                    {

                    }
                }
            }

            try
            {
                await Task.Run(() =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"stop \"{WinSrv.ServiceWorker.GetServiceName()}\"",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                });
            }
            catch
            {

            }
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
