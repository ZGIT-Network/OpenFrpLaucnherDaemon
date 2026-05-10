using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenFrp.Service;
using OpenFrp.Service.Helpers;

namespace OpenFrp.Service.Rpc
{
    public class DaemonManager
    {
        public DaemonManager(RpcManager rpcManager, ILogger<DaemonManager> logger)
        {
            this.logger = logger;
            this.rpcManager = rpcManager;

            DaemonService = GetWindowsService();
        }

        protected readonly RpcManager rpcManager;
        protected readonly ILogger<DaemonManager> logger;

        protected Task? prevListenDaemonTask;
        protected TaskCompletionSource<string>? onlineTunnelsWaiter;
        protected Process? daemon_Process3rd;

        public ServiceController? DaemonService
        {
            get; protected set;
        }

        public Process? DaemonProcess
        {
            get; protected set;
        }

        public SemaphoreSlim? Semaphore_LaunchFinish { get; private set; }

        public static ServiceController? GetWindowsService()
        {
            var installedServices = ServiceController.GetServices();
            var serviceName = OpenFrp.Service.WinSrv.ServiceWorker.GetServiceName();

            foreach (var serviceItem in installedServices)
            {
                if (!serviceItem.ServiceName.Equals(serviceName, StringComparison.Ordinal))
                {
                    continue;
                }
                return serviceItem;
            }
            return default;
        }
        public async Task WaitForConfigureAsync(CancellationToken cancellationToken = default)
        {
            if (prevListenDaemonTask is not null)
            {
                return;
            }
            if (DaemonService is not null)
            {
                await RefreshServiceStateAsync();

                if (DaemonService is not null)
                {
                    try
                    {
                        await Task.Run(() => DaemonService.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10))).WhenAnyTime(cancellationToken);

                        return;
                    }
                    catch (System.ServiceProcess.TimeoutException ex)
                    {
                        logger.LogWarning(ex, "[WaitForConfigureAsync] 等待 OpenFrp 服务启动时超时。");

                        throw;
                    }
                }
            }
            if (DaemonProcess is null & Semaphore_LaunchFinish is null)
            {
                throw new InvalidOperationException("请先启动后再进行操作。");
            }
            if (Semaphore_LaunchFinish is null)
            {
                throw new InvalidOperationException("不允许多实例同时等待。");
            }

            if (!rpcManager.IsConfigured)
            {
                try
                {
                    await Semaphore_LaunchFinish.WaitAsync(cancellationToken);
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    throw;
                }
                catch
                {

                }
            }

            return;
        }

        /// <summary>
        /// 刷新服务状态
        /// (注：当服务被[标记]删除或者不存在时，会将服务字样标为默认值。)
        /// </summary>
        public async Task RefreshServiceStateAsync()
        {
            if (DaemonService is null)
            {
                if (GetWindowsService() is { } sv) 
                {
                    DaemonService = sv;
                }
                else return;
            }

            await Task.Run(DaemonService.Refresh);

            try
            {
                _ = DaemonService.ServiceHandle;
            }
            catch (System.InvalidOperationException ex)
            {
                // 1072 = ERROR_SERVICE_MARKED_FOR_DELETE ; 1060 = ERROR_SERVICE_DOES_NOT_EXIST;
                if (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: var code })
                {
                    switch (code)
                    {
                        case 1060 or 1072:
                            DaemonService = null;
                            break;
                        case 5 when DaemonService.Status is ServiceControllerStatus.Running:
                            logger.LogDebug("[RefreshServiceState] Service Actived!");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[RefreshServiceState] 刷新 OpenFrp 服务状态时发生了错误。");
            }
        }

        /// <summary>
        /// 启动服务
        /// </summary>
        private async Task LaunchServiceAsync()
        {
            if (DaemonService is null) throw new NullReferenceException(nameof(DaemonService));

            try
            {
                if (DaemonService.Status is not ServiceControllerStatus.Running and not ServiceControllerStatus.StartPending)
                {
                    DaemonService.Start();
                }
                DaemonService.Refresh();

                return;
            }
            catch (InvalidOperationException ex)
            {
                if (ex.InnerException is System.ComponentModel.Win32Exception p)
                {
                    switch (p.NativeErrorCode)
                    {
                        case 1084:
                            logger.LogError(p, "[LaunchService] 在安全模式下暂时无法启动服务。");
                            break;
                        case 1058:
                            logger.LogError(p, "[LaunchService] OpenFrp 服务已被禁用，请启用后再试。");
                            break;
                        case 1072 or 1060:
                            DaemonService = null;
                            throw p;
                        case 5:
                            logger.LogDebug("[LaunchService] 调用辅助进程启动 Service");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[LaunchService] 启动 OpenFrp 服务时发生了错误。");
                throw;
            }

            try
            {
                var proc = await OpenFrp.Service.Helpers.ProcessHelper.StartAsync(new ProcessStartInfo()
                {
                    FileName = OpenFrp.Service.Helpers.FileHelper.GetServiceExecutableFile(),
                    Arguments = $"--service launch",
                    CreateNoWindow = true,
                    ErrorDialog = false,
                    UseShellExecute = true,
                    ErrorDialogParentHandle = IntPtr.Zero,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                if (proc is not null)
                {
                    await proc.WaitForExitAsync();

                    if (proc.HasExited && proc.ExitCode is not 0)
                    {
                        throw new System.ComponentModel.Win32Exception(proc.ExitCode);
                    }
                    else if (!proc.HasExited)
                    {
                        throw new System.InvalidOperationException("OpenFrp 服务启动失败，进程未退出。");
                    }
                    else
                    {
                        return;
                    }
                }
                throw new NullReferenceException($"无法启动 OpenFrp 服务，进程未创建。({OpenFrp.Service.Helpers.FileHelper.GetServiceExecutableFile()})");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // 5 - ERROR_ACCESS_DENIED
                switch (ex.NativeErrorCode)
                {
                    case 1084:
                        logger.LogError(ex, "[LaunchService] 在安全模式下暂时无法启动服务。");
                        break;
                    case 1058:
                        logger.LogError(ex, "[LaunchService] OpenFrp 服务已被禁用，请启用后再试。");
                        break;
                    case 1072 or 1060:
                        DaemonService = null;
                        break;
                    case 1223:
                        logger.LogDebug("[LaunchService] 用户取消了服务启动请求。");
                        throw new TaskCanceledException("用户取消了服务启动请求。",ex);
                    case 5:
                        logger.LogDebug("[LaunchService] 操作被系统拒绝");
                        throw new TaskCanceledException("操作被系统拒绝，请检查杀软是否拦截了操作。",ex);
                }
                throw;
            }
        }

        /// <summary>
        /// 关闭服务
        /// </summary>
        /// <returns></returns>
        private async Task KillServiceAsync()
        {
            if (DaemonService is null) throw new NullReferenceException(nameof(DaemonService));

            try
            {
                if (DaemonService.Status is not ServiceControllerStatus.Stopped and not ServiceControllerStatus.StopPending)
                {
                    DaemonService.Stop();
                }
                DaemonService.Refresh();

                return;
            }
            catch (InvalidOperationException ex)
            {
                if (ex.InnerException is System.ComponentModel.Win32Exception p)
                {
                    switch (p.NativeErrorCode)
                    {
                        case 1072 or 1060:
                            DaemonService = null;
                            return;
                        case 5:
                            logger.LogDebug("[KillService] 调用辅助进程关闭 Service");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[KillService] 关闭 OpenFrp 服务时发生了错误。");

                throw;
            }

            try
            {
                var proc = await OpenFrp.Service.Helpers.ProcessHelper.StartAsync(new ProcessStartInfo()
                {
                    FileName = OpenFrp.Service.Helpers.FileHelper.GetServiceExecutableFile(),
                    Arguments = $"--service stop",
                    CreateNoWindow = true,
                    ErrorDialog = false,
                    UseShellExecute = true,
                    ErrorDialogParentHandle = IntPtr.Zero,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                if (proc is not null)
                {
                    await proc.WaitForExitAsync();

                    if (proc.HasExited && proc.ExitCode is not 0)
                    {
                        throw new System.ComponentModel.Win32Exception(proc.ExitCode);
                    }
                    else if (!proc.HasExited)
                    {
                        throw new System.InvalidOperationException("OpenFrp 服务进程关闭失败，进程未退出。");
                    }
                    else
                    {
                        return;
                    }
                }
                throw new NullReferenceException($"无法关闭 OpenFrp 服务，进程未创建。({OpenFrp.Service.Helpers.FileHelper.GetServiceExecutableFile()})");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // 5 - ERROR_ACCESS_DENIED
                switch (ex.NativeErrorCode)
                {
                    case 1072 or 1060:
                        DaemonService = null;
                        return;
                    case 1223:
                        logger.LogDebug("[LaunchService] 用户取消了服务启动请求。");
                        throw new TaskCanceledException("用户取消了服务启动请求。", ex);
                    case 5:
                        logger.LogDebug("[LaunchService] 操作被系统拒绝");
                        throw new TaskCanceledException("操作被系统拒绝，请检查杀软是否拦截了操作。", ex);
                }
                throw;
            }
        }

        private async Task ListenServicesPending()
        {
            if (DaemonService is null) throw new NullReferenceException(nameof(DaemonService));

            try
            {
                if (DaemonService.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                {
                    await Task.Run(() => DaemonService.WaitForStatus(ServiceControllerStatus.Stopped));

                    prevListenDaemonTask = null;

                    rpcManager.Crack();
                    DeamonProcessExited();
                }
            }
            catch (InvalidOperationException ex)
            {
                if (ex.InnerException is System.ComponentModel.Win32Exception p)
                {
                    switch (p.NativeErrorCode)
                    {
                        case 1072 or 1060:
                            DaemonService = null;
                            return;
                        case 5:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[KillService] 关闭 OpenFrp 服务时发生了错误。");

                throw;
            }
            return;
        }

        /// <summary>
        /// 启动 Daemon (自动判断 Win 服务 / 子进程模式)
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="TaskCanceledException"></exception>
        public async Task LaunchDaemonAsync()
        {
            await RefreshServiceStateAsync();

            if (DaemonProcess is not null)
            {
                DaemonProcess.Refresh();

                if (DaemonProcess.HasExited)
                {
                    DaemonProcess = default;
                }
                else return;
            }
            
            if (DaemonService is not null)
            {
                DaemonProcess = null;

                try
                {
                    await LaunchServiceAsync();

                    prevListenDaemonTask ??= ListenServicesPending();

                    return;
                }
                catch(Exception ex)
                {
                    if (ex is not System.ComponentModel.Win32Exception { NativeErrorCode: 1060 or 1072 })
                    {
                        logger.LogError(ex, "[LaunchDaemon] 启动 OpenFrp 服务时发生了错误。");

                        throw;
                    }
                    else logger.LogDebug("[LaunchDaemon] OpenFrp 服务不存在, 回退到 子进程 Daemon 模式。");
                }
            }

            var pipeName = OpenFrp.Service.Daemon.Daemon.GetPipename();

            var mutex = new Mutex(true, $"service.{pipeName}", out var createdNewFlag);

            if (!createdNewFlag && !mutex.SafeWaitHandle.IsClosed)
            {
                if (prevListenDaemonTask is not null)
                {
                    return;
                }
                // 已经创建了一个相同命名的进程，在此监听，等到其结束。

                var processes = Process.GetProcessesByName("OpenFrp.Service");

                if (processes.Length > 0)
                {
                    string asm = FileHelper.GetServiceExecutableFile();

                    foreach (var proc in processes)
                    {
                        try
                        {
                            if (proc.GetMainModuleFileName().Equals(asm))
                            {
                                mutex.Dispose();

                                prevListenDaemonTask ??= ListenProcessUntilExit(daemon_Process3rd = proc);

                                return;
                            }
                        }
                        catch (System.ComponentModel.Win32Exception ex)
                        {
                            logger.LogWarning(ex, "[LaunchDaemon] 获取进程 (PID: {pid})的主模块文件名时发生了错误。({msg})",proc.Id,ex.Message);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex,"[LaunchDaemon] 获取进程 (PID: {pid})的主模块文件名时发生了未知错误。", proc.Id);
                        }
                    }
                }

                prevListenDaemonTask ??= ListenMutexUntilRelease(mutex);

                return;
            }
            else
            {
                mutex.Close();
            }
            
            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = OpenFrp.Service.Helpers.FileHelper.GetServiceExecutableFile(),
                        Arguments = "--daemon",
                        CreateNoWindow = true,
                        ErrorDialog = false,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        StandardErrorEncoding = System.Text.Encoding.Default,
                        StandardOutputEncoding = System.Text.Encoding.Default,
                    },
                    EnableRaisingEvents = true
                };

                proc.Exited += DaemonProcessExited;

                if (!await proc.StartAsync())
                {
                    try
                    {
                        if (proc.HasExited)
                        {
                            throw new System.ComponentModel.Win32Exception(proc.ExitCode);
                        }
                        else
                        {
                            throw new InvalidOperationException("[LaunchDaemon] 无法启动 OpenFrp 服务，进程未创建。");
                        }
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                proc.OutputDataReceived += DaemonProcessOutputDataReceived;
                proc.ErrorDataReceived += DaemonProcessOutputDataReceived;

                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();

                Semaphore_LaunchFinish = new SemaphoreSlim(0, 1);

                DaemonProcess = proc;

                return;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                switch (ex.NativeErrorCode)
                {
                    case 5:
                        logger.LogDebug("[LaunchDaemon] 操作被系统拒绝");
                        throw new InvalidOperationException("操作被系统拒绝，请检查杀软是否拦截了操作。", ex);
                    case 1223:
                        logger.LogDebug("[LaunchDaemon] 用户取消了服务启动请求。");
                        throw new TaskCanceledException("用户取消了服务启动请求。",ex);
                    default:
                        {
                            throw;
                        }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 杀死 Daemon 进程 (当 Message 为 Daemon 杀死前所开启的隧道的等效文本表示内容)
        /// </summary>
        /// <returns></returns>
        public async Task<string> KillDaemonAsync()
        {
            if (DaemonProcess is not null)
            {
                if (DaemonProcess.HasExited)
                {
                    DaemonProcess = default;

                    return string.Empty;
                }

                DaemonProcess.EnableRaisingEvents = false;

                onlineTunnelsWaiter = new TaskCompletionSource<string> { };

                try
                {
                    await DaemonProcess.StandardInput.WriteLineAsync("exitProc");

                    TryDisposeSemaphore();

                    var delay = DaemonProcess.WaitForExitAsync(3000);

                    if (await Task.WhenAny(onlineTunnelsWaiter.Task, delay) != delay && onlineTunnelsWaiter.Task.Status is TaskStatus.RanToCompletion)
                    {
                        logger.LogDebug("[KillDaemonAsync] 正在保存自启动隧道 (JSON Value): {result}", onlineTunnelsWaiter.Task.Result);

                        return await onlineTunnelsWaiter.Task;
                    }

                    return string.Empty;
                }
                catch (InvalidOperationException)
                {
                    // 可能方法被二次调用或者进程已经退出。
                }
                catch (System.IO.IOException)
                {

                }
                finally
                {
                    DaemonProcess = default;
                    onlineTunnelsWaiter?.TrySetCanceled();
                }
            }
            else if (rpcManager.IsConfigured)
            {
                var resp = await rpcManager.Sync();

                if (resp.Data is { IsLogon: true, Onlines: var onlinex, HasCurrentId: true })
                {
                    logger.LogDebug("[KillDaemonAsync] RPC 返回 | 用户 ID : #{id} , 在线隧道列表: {onlinex}", resp.Data.CurrentId, string.Join(", ", onlinex));

                    if (DaemonService is not null)
                    {
                        await KillServiceAsync();

                        return string.Empty;
                    }
                  
                    try
                    {
                        daemon_Process3rd?.Kill();

                        KillSubprocessByName();
                    }
                    catch (Exception)
                    {
                        KillProcessByName();
                    }
                    finally
                    {
                        daemon_Process3rd = default;
                    }
                    return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, int[]>()
                    {
                        {resp.Data.CurrentId.ToString(), onlinex.ToArray() }
                    });
                    
                }

    
            }

            prevListenDaemonTask = default;

            KillProcessByName();

            return string.Empty;
        }


        // 接下来的部分是在 子进程 模式在中对进程的监听和处理。

        // 只有主动创建进程才会有 Semaphore_LaunchFinish 
        private void TryDisposeSemaphore()
        {
            try
            {
                Semaphore_LaunchFinish?.Release();
                Semaphore_LaunchFinish?.Dispose();
            }
            catch (Exception) { }
            finally
            {
                Semaphore_LaunchFinish = default;
            }
        }

        private void KillSubprocessByName()
        {
            if (!FileHelper.TryGetFRPClient(out string path)) return;

            string prefix = "";

            if (Environment.OSVersion.Version is { Major: 6,Minor: 1 })
            {
                prefix = "legacy_";
            }

            foreach (var proc in Process.GetProcessesByName($"{prefix}frpc_windows_{OpenFrp.Service.Helpers.FileHelper.UserPlatform}"))
            {
                try
                {
                    if (proc.GetMainModuleFileName().Equals(path))
                    {
                        proc.Kill();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError("[KillDaemon] 获取 FRPC 进程 (PID: {pid}) 的主模块文件名时发生了错误。({msg})", proc.Id, ex.Message);
                }
            }
        }

        private void KillProcessByName()
        {
            var processes = Process.GetProcessesByName("OpenFrp.Service");

            if (processes.Length > 0)
            {
                string asm = FileHelper.GetServiceExecutableFile();

                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.GetMainModuleFileName().Equals(asm))
                        {
                            proc.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[KillDaemon] 获取进程 (PID: {pid}) 的主模块文件名时发生了未知错误。", proc.Id);
                    }
                }
            }

            if (!FileHelper.TryGetFRPClient(out string path)) return;
            
            string prefix = "";
            if (Environment.OSVersion.Version is { Major: 6, Minor: 1 })
            {
                prefix = "legacy_";
            }

            foreach (var proc in Process.GetProcessesByName($"{prefix}frpc_windows_{OpenFrp.Service.Helpers.FileHelper.UserPlatform}"))
            {
                try
                {
                    if (proc.GetMainModuleFileName().Equals(path))
                    {
                        proc.Kill();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError("[KillDaemon] 获取 FRPC 进程 (PID: {pid}) 的主模块文件名时发生了错误。({msg})", proc.Id, ex.Message);
                }
            }
        }

        private void DaemonProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is string { Length: > 0 } msg)
            {
                Debug.WriteLine($"[OF Daemon] {msg}");
                switch (msg)
                {
                    case "info: OpenFrp.Service.Daemon.Daemon.RpcServiceInterface[0] service launched!":
                        {
                            try
                            {
                                Semaphore_LaunchFinish?.Release();
                            }
                            catch (System.ObjectDisposedException)
                            {
                                break;
                            }
                        }
                        ; break;
                    case "warn: OpenFrp.Service.Daemon.Daemon.RpcServiceInterface[0] service mode detected.":
                        _ = this.RefreshServiceStateAsync();
                        break;
                    default:
                        {
                            if (msg.StartsWith("fail:"))
                            {
                                if (DaemonProcess is not { } || !DaemonProcess.WaitForExit(1000)) return;
                                DaemonProcess.Exited -= DaemonProcessExited;

                                DaemonProcessExited(DaemonProcess, msg);
                            }
                            else if (msg.StartsWith("jsonValue!of+="))
                            {
                                onlineTunnelsWaiter?.TrySetResult(msg.Substring("jsonValue!of+=".Length));
                            }
                        }
                        ; break;
                }
            }
        }

        protected virtual void DaemonProcessExited(object? sender, object? data)
        {
            if (sender is not Process process || (data is null && prevListenDaemonTask is null)) return;

            try { Semaphore_LaunchFinish?.Release(); } catch { }

            int exitCode = -1;

            rpcManager.Crack();

            try
            {
                exitCode = process.ExitCode;
            }
            catch(Exception ex)
            {
                logger.LogError(ex, "[DaemonProcessExited] 获取进程退出代码时发生了错误。");
            }




            //StringBuilder @string = new StringBuilder();

            //@string.AppendLine($"(聚焦该窗口，按下Ctrl+C 复制内容) Deamon 异常退出");
            //@string.AppendLine($"ExitCode: {exitCode}");


            //if (data is EventArgs)
            //{
            //    DaemonProcess = default;
            //}
            //switch (exitCode)
            //{
            //    case 0 or 768:
            //        {
            //            if (data is string { Length: > 0 } stdErr && string.IsNullOrWhiteSpace(stdErr))
            //            {
            //                if (stdErr.StartsWith("fail"))
            //                {
            //                    @string.AppendLine($"\n\n错误内容:\n{stdErr}\n");
            //                }
            //                else
            //                {
            //                    goto case -2;
            //                }
            //            }
            //            else if (data is EventArgs)
            //            {
            //                goto case -2;
            //            }
            //        };break;
            //    case 1 when data is EventArgs:
            //    case -2:
            //        {
            //            App.Current.Dispatcher.Invoke(() =>
            //            {
            //                Model.RouteMessage<ViewModels.MainWindowViewModel>.Send("processExit");
            //                Model.RouteMessage<ViewModels.MainWindowViewModel>.Send("processLfec");

            //                Model.RouteMessage<ViewModels.LoginWindowViewModel>.Send("processExit");
            //                Model.RouteMessage<ViewModels.LoginWindowViewModel>.Send("processLfec");
            //            });
            //            @string.Clear();
            //            return;
            //        };
            //}

            //@string.AppendLine("\n\"重试\" - 将尝试重新启动守护进程；\n\"取消\" - 退出启动器。");

            //App.Current.Dispatcher.Invoke(() =>
            //{
            //    if (App.Current.MainWindow is AppWindow ap)
            //    {
            //        ap.ShowByHANDLE();

            //        ap.WindowState = WindowState.Normal;
            //        ap.Activate();
            //    }
            //    var resp = Extend.SendMessage(App.Current.MainWindow, "OpenFrp Launcher", @string.ToString(), OpenFrp.Service.Helpers.MessageBoxHelper.MessageMode.Error | OpenFrp.Service.Helpers.MessageBoxHelper.MessageMode.RetryCancel);

            //    switch (resp)
            //    {
            //        case MessageBoxHelper.MessageResult.Cancel:
            //            ViewModels.MainWindowViewModel.ShutdownApp();
            //            break;
            //        case MessageBoxHelper.MessageResult.Retry:
            //            if (App.Current.MainWindow is MainWindow)
            //            {
            //                Model.RouteMessage<ViewModels.MainWindowViewModel>.Send("processLfec");
            //            }
            //            else if (App.Current.MainWindow is LoginWindow)
            //            {
            //                Model.RouteMessage<ViewModels.LoginWindowViewModel>.Send("processLfec");
            //            }
            //            break;
            //    }
            //});
        }

        // 无参 作为备用方案 不是 Mutex 时请勿用
        protected virtual void DeamonProcessExited()
        {
            rpcManager.Crack();

            /**
            if (App.Current.MainWindow is MainWindow)
            {
                Model.RouteMessage<ViewModels.MainWindowViewModel>.Send("processExit");
                Model.RouteMessage<ViewModels.MainWindowViewModel>.Send("processLfec");
            }
            else if (App.Current.MainWindow is LoginWindow)
            {
                Model.RouteMessage<ViewModels.LoginWindowViewModel>.Send("processExit");
                Model.RouteMessage<ViewModels.LoginWindowViewModel>.Send("processLfec");
            }
             */
        }


        internal async Task ListenProcessUntilExit(Process process,CancellationToken cancellationToken = default)
        {
            try
            {
                if (process.HasExited)
                {
                    return;
                }
                await process.WaitForExitAsync(cancellationToken);
            }
            finally
            {
                prevListenDaemonTask = default;

                DaemonProcessExited(process, null);
            }
        }

        internal async Task ListenMutexUntilRelease(Mutex mutex)
        {
            try
            {
                if (await Task.Run(mutex.WaitOne))
                {
                    mutex.Dispose();
                }
            }
            catch(AbandonedMutexException)
            {
                mutex.Close();
            }
            catch (ObjectDisposedException)
            {
                // nothing happend;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ListenMutexUntilRelease] 监听 Mutex 时发生了错误。");
                return;
            }
            finally
            {
                prevListenDaemonTask = default;

                DeamonProcessExited();
            }
        }
    }
}
