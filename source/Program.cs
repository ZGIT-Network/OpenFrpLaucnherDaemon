using System.CommandLine;
using System.Runtime.InteropServices;

namespace OpenFrp.Service
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (System.Diagnostics.Debugger.IsAttached && args.Length == 0)
            {
                args = new string[] { "--webapp" };
            }
            

            var rootCommand = new RootCommand("OpenFrp Launcher Daemon Service");

            var deamonCommand = new Command("--daemon");
            {
                deamonCommand.SetAction(Daemon.Daemon.ExecuteAsync);
            }
            var instCommand = new Command("--inst","安装 URL 协议，服务，以及 FRPC 的更新");
            {
                var valueArg = new Argument<bool>("value")
                {
                    Arity = new ArgumentArity(1,1)
                };

                var registryCommand = new Command("url-scheme-registry");
                {
                    registryCommand.Add(valueArg);
                    registryCommand.SetAction(Inst.Inst.ControlUrlSchemeRegistry);
                }
                var serviceSubCommand = new Command("service");
                {
                    var autoLaunchOption = new Option<bool>("-auto-launch")
                    {
                        Required = false,
                        DefaultValueFactory = _ => false,
                        Description = "是否自动启动服务"
                    };
                    serviceSubCommand.Add(valueArg);
                    serviceSubCommand.Add(autoLaunchOption);

                    serviceSubCommand.SetAction(Inst.Inst.ControlServiceInst);
                }
                var frpcUpdateCommand = new Command("frpc-update");
                {
                    var proxyOption = new Option<bool>("-useProxy")
                    {
                        Required = false,
                        DefaultValueFactory = _ => false,
                        Description = "是否使用系统代理"
                    };
                    var pipeOption = new Option<string>("-pipe")
                    {
                        Required = true,
                        Description = "Grpc 服务命名管道名称"
                    };

                    frpcUpdateCommand.Add(pipeOption);
                    frpcUpdateCommand.Add(proxyOption);

                    frpcUpdateCommand.SetAction(Inst.Inst.FrpcUpdateInst);
                }
                instCommand.Add(registryCommand);
                instCommand.Add(frpcUpdateCommand);
                instCommand.Add(serviceSubCommand);
            };

            

            var serviceCommand = new Command("--service","Windows 服务的操作");
            {
                var launchCommand = new Command("launch");
                {
                    launchCommand.SetAction(_ => WinSrv.ServiceWorker.LaunchService());
                    serviceCommand.Add(launchCommand);
                }
                var stopCommand = new Command("stop");
                {
                    stopCommand.SetAction(_ => WinSrv.ServiceWorker.KillService());
                    serviceCommand.Add(stopCommand);
                }


                serviceCommand.SetAction(_ => WinSrv.ServiceWorker.ExecuteAsync());
            }

            var webAppCommand = new Command("--webapp", "提供本地可视化 Web 面板");
            {
                webAppCommand.SetAction(WebApp.WebApp.ExecuteAsync);
            };

            rootCommand.Add(deamonCommand);
            rootCommand.Add(instCommand);
            rootCommand.Add(serviceCommand);
            rootCommand.Add(webAppCommand);

            var ps = rootCommand.Parse(args);

            return await ps.InvokeAsync();
        }
    }

    public static class ExtendMethods
    {
#if NETFRAMEWORK
        public static bool IsNotNullOrEmpty(this string? str) => !string.IsNullOrEmpty(str);

        public static async Task<T?> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
        {
            Task tk = await Task.WhenAny(Task.Delay(timeout), task);
            if (tk.Equals(task)) { return await task; }

            return default;
        }

        public static async Task<T?> WithTimeout<T>(this Task<T> task, int delay)
        {
            Task tk = await Task.WhenAny(Task.Delay(delay), task);

            if (tk.Equals(task)) { return await task; }

            return default;
        }


#endif
        public static Task WhenAnyTime(this Task task, CancellationToken token)
        {
            try
            {
                return Task.WhenAny(task, token.WaitCancellationToken());
            }
            catch
            {
                return Task.CompletedTask;
            }
        }
        public static async Task<T?> WhenAnyTime<T>(this Task<T>? task, CancellationToken cancellationToken)
        {
            try
            {
                if (task is null) return default;

                var task2 = await Task.WhenAny(task, cancellationToken.WaitCancellationToken());
                if (task2.Equals(task))
                {
                    return task.Result;
                }
            }
            catch
            {
               
            }
            return default;
        }



        public static async Task WithTimeout(this Task task, TimeSpan timeout)
        {
            await Task.WhenAny(Task.Delay(timeout), task);
           
        }

        public static async Task WithTimeout(this Task task, int delay)
        {
            await Task.WhenAny(Task.Delay(delay), task);

        }


        public static async Task<T?> WaitTaskCompletionSource<T>(this TaskCompletionSource<T> source,CancellationToken token = default)
        {
#if NETFRAMEWORK
            var finishTask = await Task.WhenAny(token.WaitCancellationToken(), source.Task);
            
            if (finishTask.Equals(source.Task))
            {
                try
                {
                    return await source.Task;
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    
                }
                catch
                {
                    throw;
                }
            }

            source.TrySetCanceled(token);

            return default;
#elif NETCOREAPP
            try
            {
                var result = await source.Task.WaitAsync(token);

                if (result is { })
                {
                    return result;
                }
            }
            catch(System.Threading.Tasks.TaskCanceledException)
            {
                source.TrySetCanceled(token);
            }
            return default;
#endif
        }

        public static Task WaitCancellationToken(this CancellationToken token)
        {
            return Task.Run(() => token.WaitHandle.WaitOne());
        }

        public static async Task<Yue3.Model.Result.HttpResponse> TranslateModelResult<T>(this Task<Yue3.Model.Result.HttpResponse<T>> task) where T : class
        {
            if (await task is { } httpResponse)
            {
                string? message = httpResponse.Message;
                var code = httpResponse.StatusCode;
                switch (httpResponse.Data)
                {
                    case Yue3.Model.NatayarkAuth.Response.BaseResponse brp1:
                        {
                            message ??= brp1.Message;
                            code |= brp1.Code;
                        }; break;
                    case Yue3.Model.OpenFrp.Response.BaseResponse brp2:
                        {
                            message ??= brp2.Message;
                        }; break;
                }
                return new Yue3.Model.Result.HttpResponse
                {
                    Exception = httpResponse.Exception,
                    Message = message,
                    StatusCode = code,
                };
            }
            else if (task.Exception is { } ex)
            {
                return new Yue3.Model.Result.HttpResponse
                {
                    Exception = ex,
                };
            }
            throw new ArgumentException();
        }
    }
}
