using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Grpc.Core;
using System.Linq.Expressions;


namespace OpenFrp.Service.Manager.Process
{
    public class ProcessManager
    {
        public ProcessManager(Log.LogManager logManager,ILogger<ProcessManager> logger,ILoggerFactory loggerFactory)
        {
            this.remoteLogManager = logManager;

            this.logger = logger;
            this.loggerFactory = loggerFactory;

            this.processes = new Dictionary<int, ProcessContainer>();
        }

        // 这个是用于进行与启动器之间的日志交换 //
        private readonly Log.LogManager remoteLogManager;

        private readonly ILogger<ProcessManager> logger;
        private readonly ILoggerFactory loggerFactory;
        private readonly Dictionary<int, ProcessContainer> processes = new Dictionary<int, ProcessContainer>();

        public event ProcessStateChanged OnProcessStateChanged = delegate { };
        public event EventHandler<Exception> Error = delegate { };

        internal void HandleError(Exception ex) => Error.Invoke(this, ex);

        public bool ContainKey(int key) => processes.ContainsKey(key);

        public int[] GetOnlineProcesses() => processes.Keys.ToArray();

        public ProcessContainer[] GetContainers(Func<KeyValuePair<int,ProcessContainer>, bool> condition) => processes.Where(condition).Select(x => x.Value).ToArray();

        public ProcessContainer? GetContainer(int key)
        {
            if (processes.TryGetValue(key, out var pc))
            {
                return pc;
            }
            return default;
        }

        public async Task<object> LaunchTunnel(string userToken,string? tomlConfig,
          Yue3.Model.OpenFrp.Response.Data.UserTunnel tunnel,
          LaunchConfig config)
        {
            if (!string.IsNullOrEmpty(tomlConfig))
            {
                try
                {
                    string path = Helpers.FileHelper.GetFrpcWorkDictionary(tunnel.Id.ToString());
                    string fp = Path.Combine(path, "frpc.toml");
                    if (File.CreateText(fp) is { } stream)
                    {
                        await stream.WriteLineAsync(tomlConfig);
                        await stream.FlushAsync();

#if NET
                        await stream.DisposeAsync();
#else
                        stream.Dispose();
#endif
                        config.SetConfigFilePath(fp);
                    }
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            return await Task.Run(() => LaunchTunnel(userToken, tunnel, config));
        }

        public object LaunchTunnel(string userToken, 
            Yue3.Model.OpenFrp.Response.Data.UserTunnel tunnel, 
            LaunchConfig config)
        {
            logger.LogInformation("Launch tunnel: {userToken} - {tunnelName}", userToken, tunnel.Name);

            if (string.IsNullOrEmpty(userToken))
            {
                return new Proto.Response.BaseResponse
                {
                    Message = "请先通过创建 TunnelStream 后再进行启动。"
                };
            }
            
            if (processes.ContainsKey(tunnel.Id))
            {
                return false;
            }
            else if (Service.Helpers.FileHelper.TryGetFRPClient(out string frpcPath))
            {
                var n = new System.Diagnostics.Process
                {
                    EnableRaisingEvents = true,
                    StartInfo = new ProcessStartInfo
                    {
                        ErrorDialog = false,
                        StandardErrorEncoding = Encoding.UTF8,
                        StandardOutputEncoding = Encoding.UTF8,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        CreateNoWindow = true,
                        
                        
                        FileName = frpcPath,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = Helpers.FileHelper.GetFrpcWorkDictionary(tunnel.Id.ToString()),
                        Arguments = config.GenerateArgument(userToken,tunnel.Id),
                    }
                };

                
                try
                {
                    if (!n.Start())
                    {
                        return new Proto.Response.BaseResponse
                        {
                            Message = $"无法完成启动，请查看是否被拦截？"
                        };
                    }

                    var v = new ProcessContainer(this,remoteLogManager, n,tunnel,config, loggerFactory.CreateLogger<ProcessContainer>());


     
                    n.ErrorDataReceived += v.StdError;
                    n.OutputDataReceived += v.StdOutput;
               

                    n.BeginErrorReadLine();
                    n.BeginOutputReadLine();

                    processes.Add(tunnel.Id, v);

                    OnProcessStateChanged.Invoke(v, ProcessStateChangedType.Launch);

                    if (config.IsFastLaunch)
                    {
                        logger.LogInformation("[ProcessManager] FastLaunch Tunnel Fallback ::");

                        return new Proto.Response.TunnelStreamResponse.Types.AnonymousTunnelResponse
                        {
                            IsNewCreate = true,
                            Datas =
                            {
                                new Proto.Response.TunnelStreamResponse.Types.AnonymousTunnelResponse.Types.AnonymousTunnelData
                                {
                                    TunnelId = tunnel.Id,
                                    ConnectAddresses = tunnel.ConnectAddress,
                                    Name = tunnel.Name
                                }
                            }
                        };
                    }
                    return true;
                }
                catch(System.ComponentModel.Win32Exception ex)
                {
                    return new Proto.Response.TunnelStreamResponse.Types.TunnelControlFailed()
                    {
                        TunnelId = tunnel.Id,
                        TunnelName = tunnel.Name,
                        DebugInfo = Google.Protobuf.WellKnownTypes.Any.Pack(ex.ToRpcDebugInfo())
                    };
                }
                catch(Exception ex)
                {
                    return new Proto.Response.TunnelStreamResponse.Types.TunnelControlFailed()
                    {
                        TunnelId = tunnel.Id,
                        TunnelName = tunnel.Name,
                        DebugInfo = Google.Protobuf.WellKnownTypes.Any.Pack(ex.ToRpcDebugInfo())
                    };
                }
            }
            else
            {
                return new Proto.Response.BaseResponse
                {
                    Message = $"FRPC 文件丢失，请尝试重启启动器。(Path: {frpcPath})"
                };
            }
        }


        public object CloseTunnel(Yue3.Model.OpenFrp.Response.Data.UserTunnel tunnel)
        {
            if (processes.TryGetValue(tunnel.Id,out var psv))
            {
                logger.LogInformation("Close tunnel: {tunnelName}", tunnel.Name);

                OnProcessStateChanged.Invoke(psv, ProcessStateChangedType.Close);

                psv.Close();

                processes.Remove(tunnel.Id);
            }
            else
            {
                
            }
            return true;
        }

        public void Shutdown()
        {
            foreach(var pm in processes.Values)
            {
                try
                {
                    pm.Process.EnableRaisingEvents = false;
                    pm.Process.Kill();
                }
                catch
                {

                }
                
            }
        }

        public delegate void ProcessStateChanged(ProcessContainer container, ProcessStateChangedType toState);

        public enum ProcessStateChangedType
        {
            Launch,
            Close
        }
    }
}
