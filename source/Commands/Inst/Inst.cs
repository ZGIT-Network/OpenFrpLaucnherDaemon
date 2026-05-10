using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace OpenFrp.Service.Inst
{
    public class Inst
    {
        private static ServiceProvider? _serviceProvider;

        private static ILogger<Inst>? logger;

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        internal static void ConfigureInst()
        {
            _serviceProvider = new ServiceCollection()
               .AddLogging(builder =>
               {
                   builder.AddConsole();
                   builder.AddDebug();
                   builder.SetMinimumLevel(LogLevel.Debug);
               })
               .BuildServiceProvider();

            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();

            logger = loggerFactory.CreateLogger<Inst>();
        }

        internal static void ControlUrlSchemeRegistry(ParseResult result)
        {
            bool flag= result.GetValue<bool>("value");

            if (!IsAdministrator())
            {
                return;
            }
            ConfigureInst();

            if (logger is null)
            {
                throw new NullReferenceException("Logger");
            }

            if (flag)
            {
                try
                {
                    if (Microsoft.Win32.Registry.ClassesRoot.CreateSubKey("openfrp", true) is { } sub)
                    {
                        sub.SetValue("", "OpenFrp Launcher WPF - 桌面预览版");
                        sub.SetValue("URL Protocol", "");

                        var t = sub.CreateSubKey("shell").CreateSubKey("open").CreateSubKey("command");

                        t.SetValue("", $"{System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpenFrp.Launcher.exe")} %1");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unable to toggle registry value");
                }
            }
            else
            {
                try
                {
                    Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree("openfrp", false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unable to toggle registry value");
                }
            }
            return;
        }

        internal static async Task<int> ControlServiceInst(ParseResult result)
        {
            bool flag = result.GetValue<bool>("value");

            bool autoLaunch = result.GetValue<bool>("-auto-launch");

            if (!IsAdministrator())
            {
                return 5;
            }
#if DEBUG
            ConfigureInst();
#endif


            var hSCM = Win32Native.OpenSCManagerA(null!, null!, (uint)Win32Native.ServiceManagerAccess.SC_MANAGER_CREATE_SERVICE | (uint)Win32Native.ServiceManagerAccess.SC_MANAGER_ALL_ACCESS);

            if (hSCM == IntPtr.Zero)
            {
                return Marshal.GetLastWin32Error();
            }

            var serviceName = WinSrv.ServiceWorker.GetServiceName();

            // try to openservice,if exists, the function will return not null handle;
            var hService = Win32Native.OpenServiceA(hSCM, serviceName, (uint)Win32Native.ServiceManagerAccess.SC_MANAGER_ALL_ACCESS);

            if (flag)
            {
                // Install service
                var dir = new DirectoryInfo(Helpers.FileHelper.GetServiceExecutableFile());

                var acl = dir.GetAccessControl(AccessControlSections.Access);
                acl.SetAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), FileSystemRights.FullControl, InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
                
                dir.SetAccessControl(acl);

                if (hService == IntPtr.Zero)
                {
                    // service does not exist, we can create it

                    hService = Win32Native.CreateServiceA(
                        hSCM,
                        serviceName,
                        WinSrv.ServiceWorker.GetServiceDisplayName(),
                        (uint)Win32Native.ServiceManagerAccess.SC_MANAGER_ALL_ACCESS,
                        (uint)Win32Native.ServiceType.SERVICE_WIN32_OWN_PROCESS,
                        (uint)Win32Native.ServiceStartType.SERVICE_AUTO_START,
                        (uint)Win32Native.ServiceErrorControl.SERVICE_ERROR_NORMAL,
                        $"\"{Helpers.FileHelper.GetServiceExecutableFile()}\" --service",
                        null!, IntPtr.Zero, null!, null!, null!);

                    if (hService == IntPtr.Zero)
                    {
                        // failed to create service
                        var errorCode = Marshal.GetLastWin32Error();
                        switch (errorCode)
                        {
                            case (int)Win32Native.ControlServiceResult.ERROR_SERVICE_EXISTS:
                                logger?.LogWarning("Service already exists, but failed to open it. Error code: {errorCode}", errorCode);
                                break;
                            case (int)Win32Native.ControlServiceResult.ERROR_SERVICE_MARKED_FOR_DELETE:
                                logger?.LogWarning("Service already exists, but marked for deletion. Error code: {errorCode}", errorCode);
                                break;
                            default:
                                logger?.LogError("Failed to create service. Error code: {errorCode}", errorCode);
                                break;
                        }
                        Win32Native.CloseServiceHandle(hSCM);

                        return errorCode;
                    }
                    // service created successfully, continue working.
                    var descriptionStruct = new Win32Native.SERVICE_DESCRIPTION
                    {
                        lpDescription = "OpenFrp Launcher 桌面端服务 (中括号为服务代号)\n\n帮助文档: https://docs.openfrp.net/use/desktop-launcher\nOpenFRP官网: https://www.openfrp.net"
                    };
                    Win32Native.ChangeServiceConfig2A(hService, (int)Win32Native.ServiecConfigType.SERVICE_CONFIG_DESCRIPTION,ref descriptionStruct);
                }

                try
                {
                    var buffer = Array.Empty<byte>();
                    // Query service object security descriptor
                    if (!Win32Native.QueryServiceObjectSecurity(hService, System.Security.AccessControl.SecurityInfos.DiscretionaryAcl, buffer, 0, out var size))
                    {
                        var errorCode = Marshal.GetLastWin32Error();

                        if (errorCode != (int)Win32Native.ControlServiceResult.ERROR_INSUFFICIENT_BUFFER && errorCode != 0)
                        {
                            logger?.LogError("Failed to query service object security descriptor. Error code: {errorCode}", errorCode);
                        }

                        buffer = new byte[size];

                        if (!Win32Native.QueryServiceObjectSecurity(hService, System.Security.AccessControl.SecurityInfos.DiscretionaryAcl, buffer, size, out size))
                        {
                            logger?.LogError("Failed to query service object security descriptor. Error code: {errorCode}", errorCode = Marshal.GetLastWin32Error());

                            return errorCode;
                        }
                    }

                    var rawAcl = new RawSecurityDescriptor(buffer, 0);

                    var dacl = new DiscretionaryAcl(false, false, rawAcl.DiscretionaryAcl);

                    var perm = (int)(Win32Native.ServiceAccessRights.SERVICE_QUERY_STATUS |
                        Win32Native.ServiceAccessRights.SERVICE_START |
                        Win32Native.ServiceAccessRights.SERVICE_STOP |
                        Win32Native.ServiceAccessRights.SERVICE_INTERROGATE);
                    dacl.SetAccess(AccessControlType.Allow, new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), perm, InheritanceFlags.None, PropagationFlags.None);

                    buffer = new byte[dacl.BinaryLength];
                    dacl.GetBinaryForm(buffer, 0);

                    rawAcl.DiscretionaryAcl = new RawAcl(buffer, 0);

                    buffer = new byte[rawAcl.BinaryLength];

                    rawAcl.GetBinaryForm(buffer, 0);

                    if (Win32Native.SetServiceObjectSecurity(hService, System.Security.AccessControl.SecurityInfos.DiscretionaryAcl, buffer))
                    {
                        if (autoLaunch)
                        {
                            var v2 = WinSrv.ServiceWorker.LaunchService(hService);

                            if (v2 != 0)
                            {
                                // https://github.com/xcp-ng/xenadmin/blob/79377cf549c3e7201caee9ae41edf6004c5ffe47/XenCenterLib/Win32.cs#L348

                                logger?.LogError("Failed to launch service. Error code: {v2} ({reason})", v2, Win32Native.GetFormatMessage((uint)v2));
                            }

                            logger?.LogInformation("Service ACL updated successfully.");


                            return (int)v2;
                        }
                        return 0;
                    }
                    else
                    {
                        var errorCode = Marshal.GetLastWin32Error();

                        logger?.LogError("Failed to update service ACL. Error code: {errorCode}", errorCode);

                        return errorCode;
                    }
                }
                finally
                {
                    Win32Native.CloseServiceHandle(hSCM);
                    Win32Native.CloseServiceHandle(hService);
                }

                // ACL control:
                // 上方是对服务的 ACL 进行修改，在普通情况下无需 UAC 控制权限即可运行服务。（即使是非管理员用户）

                // A: Access Allowed
                // LC:  SERVICE_QUERY_STATUS (查询状态)
                // RP:  SERVICE_START (启动)
                // WP:  SERVICE_STOP (停止)
                // LO:  SERVICE_INTERROGATE (一系列控制服务的操作)
                // AU: Authenticated User 

                // Thanks below projects for reference:
                // https://github.com/dmcxblue/SharpBlackout/blob/7ac51806d8a133586b827e7f04847aca1e2237e9/SharpBlackOut/Program.cs#L116
                // https://github.com/natfrp/launcher-windows/blob/072803051e81530b5d94c3130f28ed0e18a22a41/SakuraFrpService/Program.cs#L43
                // https://github.com/ryanmrestivo/red-team/blob/1e53b7aa77717a22c9bd54facc64155a9a4c49fc/Malware-Development/SharpUp/SharpUp/Program.cs#L627
            }
            else if (hService != IntPtr.Zero)
            {
                try
                {
                    var v2 = WinSrv.ServiceWorker.KillService(hService);

                    if (v2 != 0)
                    {
                        logger?.LogError("Failed to stop service. Error code: {v2} ({reason})", v2, Win32Native.GetFormatMessage((uint)v2));

                        return (int)v2;
                    }

                    if (!Win32Native.DeleteService(hService))
                    {
                        var errorCode = Marshal.GetLastWin32Error();

                        logger?.LogError("Failed to delete service. Error code: {v2} ({reason})", errorCode, Win32Native.GetFormatMessage((uint)errorCode));

                        return errorCode;
                    }

                    return 0;
                }
                finally
                {
                    Win32Native.CloseServiceHandle(hSCM);
                    Win32Native.CloseServiceHandle(hService);
                }
            }
            else if (flag)
            {
                await WinSrv.ServiceWorker.LaunchService();
            }
            else
            {
                await WinSrv.ServiceWorker.KillService();
            }
            return 0;
        }

        internal static async Task FrpcUpdateInst(ParseResult result)
        {
            string pipeName = result.GetRequiredValue<string>("-pipe");
            bool useProxy = result.GetValue<bool>("-useProxy");
            if (!IsAdministrator())
            {
                return;
            }
            ConfigureInst();

            if (logger is null)
            {
                throw new NullReferenceException("Logger");
            }

            logger.LogDebug("[Frpc DownloadProc] Pipe: {pipeName}", pipeName);

            Console.CancelKeyPress += (_, e) =>
            {
                Console.ResetColor();
            };
            Console.Title = "[OpenFrp] Downloading FRPC: " + Helpers.FileHelper.FrpcDirectory;

            var f = Directory.CreateDirectory(Helpers.FileHelper.FrpcDirectory);
            var p = new Progress<Net.HttpClient.HttpDownloadProgress>();

            if (!useProxy)
            {
                Net.HttpClient.DefualtInstance.SetUseProxy(false);
            }

            var channel = new GrpcDotNetNamedPipes.NamedPipeChannel(".", pipeName, new GrpcDotNetNamedPipes.NamedPipeChannelOptions
            {
                ConnectionTimeout = 10
            });


            var pnc = new Proto.BackgroundService.OpenFrpBackgroundService.OpenFrpBackgroundServiceClient(channel);

            try
            {
                await pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback 
                { 
                    State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.Messaging,
                    Data = Any.Pack(new StringValue()
                    {
                        Value = "service worker is loaded"
                    }) 
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to connect rpc service.");
                return;
            }

            var t = await Net.OpenFrpApi.GetSoftwareConfig();

            if (t.StatusCode is not System.Net.HttpStatusCode.OK || t.Data is not { DownloadSources: { Length: > 0 } source } softwareConfig)
            {
                logger.LogError("获取配置失败: ({code}) : {message} ", t.StatusCode, t.Message);
                logger.LogError("获取配置失败: Header = {header} ", t.Headers?.ToString());
                logger.LogError(t.Exception, "获取配置失败: ");

                await pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                {
                    State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.Messaging,
                    Data = Any.Pack(t.Exception is null ? new StringValue() { Value = t.Message } : t.Exception.ToRpcDebugInfo())
                });
                return;
            }

            p.ProgressChanged += (_, e) =>
            {
                if (e.IsIndeterminate)
                {
                    pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                    {
                        State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.ProgressValue,
                        Data = Any.Pack(new Value
                        {
                            NumberValue = 0
                        })
                    });
                    return;
                }
                if (e.ReadLength is not 0)
                {
                    pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                    {
                        State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.ProgressValue,
                        Data = Any.Pack(new Value
                        {
                            NumberValue = 101 - (e.TotalLength / e.ReadLength)
                        })
                    });
                }
            };

            bool installed = false;
            string targetVersion = softwareConfig.Latest!;

            if (Environment.OSVersion.Version.Major is 6 && Environment.OSVersion.Version.Minor < 2)
            {
                targetVersion = "OpenFRP_0.54.0_835276e2_20240205";
            }

            int index = source.Length;
            foreach (var ap in source)
            {
                index--;
                string url = $"{ap.BaseUrl}/{targetVersion}/frpc_windows_{Service.Helpers.FileHelper.UserPlatform}.zip";

                await pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                {
                    State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.SwitchSource,
                    Data = Any.Pack(new StringValue { Value = url })
                });

                using var ms = new MemoryStream();

                var req = await Net.HttpClient.DefualtInstance.GetAsync(url,ms, p);

                if (req is { StatusCode: System.Net.HttpStatusCode.OK, Message: not null } && req.Message.Contains("application/zip") &&
                    ms.Length > 0)
                {
                    ms.Seek(0, SeekOrigin.Begin);

                    await pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                    {
                        State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.ProgressValue,
                        Data = Any.Pack(new Value
                        {
                            NumberValue = 100
                        })
                    });

                    if (Helpers.FileHelper.TryGetFRPClient(out var fp))
                    {
                        try { File.Delete(fp); } catch { }
                    }
                    try
                    {
                        using (ZipArchive ac = new ZipArchive(ms, ZipArchiveMode.Read, false))
                        {
#if NET
                            ac.ExtractToDirectory(Helpers.FileHelper.FrpcDirectory,true);
#else
                            foreach (ZipArchiveEntry entry in ac.Entries)
                            {
                                entry.ExtractToFile(Path.Combine(Helpers.FileHelper.FrpcDirectory, entry.FullName), true);
                            }
#endif
                            if (Helpers.FileHelper.TryGetFRPClient(out string path))
                            {
                                var proc = Process.Start(new ProcessStartInfo
                                {
                                    FileName = path,
                                    Arguments = "-v",
                                    CreateNoWindow = true,
                                    UseShellExecute = false,
                                    WindowStyle = ProcessWindowStyle.Hidden
                                }) ?? throw new System.ComponentModel.Win32Exception("Failed to execute FRPC process. Please check if the executable is blocked by antivirus or other security software.");
#if NET
                                await proc.WaitForExitAsync();
#else
                                await Task.Run(proc.WaitForExit);
#endif
                                // 这里等待FRPC进程退出，确保它已经被正确安装
                                // 如果被例如 Windows Defender 或其他杀毒软件阻止了，可能会导致进程无法启动
                                // 并返回 Win32Exception (NativeErrorCode = 225 or 226)
                                // 文档: https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-
                                // 225: ERROR_VIRUS_INFECTED
                                // 226: ERROR_VIRUS_DELETED
                            }
                            else
                            {
                                throw new System.ComponentModel.Win32Exception("FRPC executable not found in the extracted files or maybe blocked by antivirus.");
                            }


                            installed = true;

                            if (Environment.OSVersion.Version.Major is 6 && Environment.OSVersion.Version.Minor < 2)
                            {
                                var ef = Path.Combine(Helpers.FileHelper.FrpcDirectory, $"frpc_windows_{Helpers.FileHelper.UserPlatform}.exe");
                                var rf = Path.Combine(Helpers.FileHelper.FrpcDirectory, $"legacy_frpc_windows_{Helpers.FileHelper.UserPlatform}.exe");

                                if (File.Exists(rf))
                                {
                                    File.Delete(rf);
                                }
                                if (File.Exists(ef))
                                {
                                    File.Move(ef, rf);
                                    File.Delete(ef);
                                }
                            }

                        }
                        break;
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        switch (ex.NativeErrorCode)
                        {
                            case 2: // File not found
                                await pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                                {
                                    State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.Messaging,
                                    Data = Any.Pack(new StringValue() { Value = "err: 安装失败，无法找到FRPC可执行文件，请检查是否被杀毒软件误杀或隔离。" })
                                });
                                logger.LogError(ex, "安装失败，无法找到FRPC可执行文件，请检查是否被杀毒软件误杀或隔离。");
                                return;
                            case 225 or 226: // ERROR_VIRUS_INFECTED or ERROR_VIRUS_DELETED
                                await pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                                {
                                    State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.Messaging,
                                    Data = Any.Pack(new StringValue() { Value = "err: 安装失败，FRPC可执行文件被杀毒软件误报，请检查杀毒软件隔离或排除该文件。" })
                                });
                                return;
                        }
                    }
                    catch (Exception ex)
                    {
                        await pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                        {
                            State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.Messaging,
                            Data = Any.Pack(ex.ToRpcDebugInfo())
                        });
                        logger.LogError(ex, "安装失败，请尝试重新安装。");
                    }
                    
                }
                else
                {
                    await pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                    {
                        State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.Messaging,
                        Data = Any.Pack(req.Exception is null ? new StringValue() { Value = req.Message ?? "未知错误" } : req.Exception.ToRpcDebugInfo())
                    });
                    logger.LogError("下载失败: ({code}) : {message} ", req.StatusCode, req.Message);
                    logger.LogError("下载失败: Header = {header} ", req.Headers?.ToString());
                    logger.LogWarning("如有可用的源，下载器将会切换到下一个源下载。");
                }
                if (index > 0)
                {
                    await Task.Delay(1500);
                }
            }

            if (installed)
            {
                await pnc.DownloadServiceFallbackAsync(new Proto.Request.DownloadFallback
                {
                    State = Proto.Request.DownloadFallback.Types.DownloadFallbackType.Messaging,
                    Data = Any.Pack(new StringValue() { Value = "finishDownload" })
                });
            }

            
        }
    }
}
