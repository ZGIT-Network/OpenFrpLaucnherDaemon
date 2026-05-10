using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using OpenFrp.Service.Proto.Request;
using OpenFrp.Service.Proto.Response;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Nito.AsyncEx;


namespace OpenFrp.Service.Daemon;

public class OpenFrpService : Proto.Service.OpenFrp.OpenFrpBase
{
    public OpenFrpService(
        Manager.Log.LogManager logManager,
        Manager.Process.ProcessManager processManager,
        Manager.Frpc.FrpcManager frpcManager,
        ILogger<OpenFrpService> logger)
    {

        this.logger = logger;

        this.rpcLogManager = logManager;

        rpcLogManager.OnNewLogPosted += RpcLogManager_OnNewLogPosted;

        this.processManager = processManager;
        this.frpcManager = frpcManager;
        
        this.processManager.Error += ProcessManager_Error;
        this.processManager.OnProcessStateChanged += ProcessManager_OnProcessStateChanged;

        ThreadPool.QueueUserWorkItem(delegate { ConsoleRedirect_Helper(); });

    }
    private void ConsoleRedirect_Helper()
    {
        while (Console.IsInputRedirected)
        {
            string? input = Console.ReadLine();
            if (input is null)
            {
                return;
            }
            if (input is "exitProc")
            {
                if (Console.IsOutputRedirected)
                {
                    if (processManager.GetOnlineProcesses() is int[] online && PrevUserInfo != null)
                    {
                        Console.Write("jsonValue!of+=" + System.Text.Json.JsonSerializer.Serialize(new Dictionary<int, int[]>
                        {
                            { PrevUserInfo.UserID,online }
                        }));
                    }
                    
                }
                processManager.Shutdown();

                Environment.Exit(0);
                return;
            }
        }

        
    }

    private void ProcessManager_OnProcessStateChanged(Manager.Process.ProcessContainer container, Manager.Process.ProcessManager.ProcessStateChangedType toState)
    {
        if (tunnelResponseWriter is null) return;

        try
        {
            logger.LogInformation("[ProcessMangaer] \"ProcessManager_OnProcessStateChanged\" invoked: ({state}) {container}",toState, container.UserTunnel.Name);
            switch (toState)
            {
                case Manager.Process.ProcessManager.ProcessStateChangedType.Launch:
                    {
                        _ = tunnelResponseWriter?.WriteAsync(new TunnelStreamResponse
                        {
                            State = TunnelStreamResponse.Types.TunnelStreamResponseState.UpdateTunnel,
                            Data = Any.Pack(new Int32Value { Value = container.UserTunnel.Id })
                        });
                    }; break;
                case Manager.Process.ProcessManager.ProcessStateChangedType.Close:
                    {
                        _ = tunnelResponseWriter?.WriteAsync(new TunnelStreamResponse
                        {
                            State = TunnelStreamResponse.Types.TunnelStreamResponseState.UpdateTunnel,
                            Data = Any.Pack(new Int32Value { Value = -container.UserTunnel.Id })
                        });
                    }; break;
            }
        }
        catch
        {

        }
    }
    private void ProcessManager_Error(object? sender,Exception ex)
    {
        if (tunnelResponseWriter is null) return;

        try
        {
            _ = tunnelResponseWriter.WriteAsync(new TunnelStreamResponse
            {
                State = TunnelStreamResponse.Types.TunnelStreamResponseState.Messaging,
                Data = Any.Pack(ex.ToRpcDebugInfo())
            });
        }
        catch
        {

        }
    }
    private void RpcLogManager_OnNewLogPosted(object? sender, LogStreamResponse.Types.LogContainer e)
    {
        _ = NewPostedAfter_Notification(e); _ = NewPostedAfter_LogStream(e);
    }
    private async Task NewPostedAfter_Notification(LogStreamResponse.Types.LogContainer e)
    {
        try
        {
            if (notificationResponseWriter is null) return;

            if (e.Data.Length > 0 && processManager.GetContainer(e.LogId) is { UserTunnel: var tunnel } cl)
            {
                switch (e.Level)
                {
                    case LogStreamResponse.Types.LogContainer.Types.LogLevel.Error:
                        {
                            await notificationResponseWriter.WriteAsync(new NotificationStreamResponse
                            {
                                State = NotificationStreamResponse.Types.NotificationStreamResponseState.LaunchFailed,
                                Data = Any.Pack(new NotificationStreamResponse.Types.LaunchFailedMsg
                                {
                                    Content = e.Data,
                                    TunnelId = tunnel.Id,
                                    TunnelName = tunnel.Name,
                                })
                            });
                            return;
                        };
                    case LogStreamResponse.Types.LogContainer.Types.LogLevel.Warning:
                        {
                            if (e.Data.StartsWith("进程在极短的时间内重启"))
                            {
                                await notificationResponseWriter.WriteAsync(new NotificationStreamResponse
                                {
                                    State = NotificationStreamResponse.Types.NotificationStreamResponseState.Messaging,
                                    Data = Any.Pack(new NotificationStreamResponse.Types.UIWarningNotice
                                    {
                                        Title = $"请注意隧道 {tunnel.Name}",
                                        Data = e.Data.Substring(4)
                                    })
                                });
                            }
                            return;
                        }
                }
                if (e.Data.Contains("启动成功"))
                {
                    var dom2 = tunnel.Domains.Where(x => !x.Equals(tunnel.ConnectAddress));
                    await notificationResponseWriter.WriteAsync(new NotificationStreamResponse
                    {
                        State = NotificationStreamResponse.Types.NotificationStreamResponseState.LaunchSuccess,
                        Data = Any.Pack(new NotificationStreamResponse.Types.LaunchSuccessMsg
                        {
                            ConnectAddresses =
                            {
                                tunnel.ConnectAddress,
                                dom2
                            },
                            ExtraConnectAddress =
                            {
                                tunnel.ExtraConnectAddress
                            },
                            TunnelId = tunnel.Id,
                            TunnelName = tunnel.Name,
                            Host = tunnel.Host ?? "",
                            Port = tunnel.Port,
                            TunnelType = tunnel.Type ?? "",
                            IsFastLaunch = cl.IsFastLaunch,
                            IsAutoLaunch = cl.IsAutoLuanch,
                        })
                    });
                }
            }
        }
        catch(Exception ex)
        {
            rpcLogManager.WriteLog(0, "Service", $"在传递启动消息时发生了错误:\n{ex.StackTrace}", level: LogStreamResponse.Types.LogContainer.Types.LogLevel.Error);
            logger.LogError(ex,"[ProcessManager] Exception was handled at NewPostedAfter_Notification()");
        }
    }
    private async Task NewPostedAfter_LogStream(LogStreamResponse.Types.LogContainer e)
    {
        try
        {
            if (logResponseWriter is null) return;

            await logResponseWriter.WriteAsync(new LogStreamResponse
            {
                State = LogStreamResponse.Types.LogStreamResponseState.UpdateLogs,
                Data = Any.Pack(e)
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            rpcLogManager.WriteLog(0, "Service", $"在传递启动消息时发生了错误:\n{ex.StackTrace}", level: LogStreamResponse.Types.LogContainer.Types.LogLevel.Error);
            logger.LogError(ex, "exception happend when post new logs...");
        }
    }

    private readonly ILogger<OpenFrpService> logger;
    private readonly Manager.Log.LogManager rpcLogManager;
    private readonly Manager.Frpc.FrpcManager frpcManager;
    private readonly Manager.Process.ProcessManager processManager;

    private IServerStreamWriter<NotificationStreamResponse>? notificationResponseWriter;
    private IServerStreamWriter<LogStreamResponse>? logResponseWriter;
    private IServerStreamWriter<TunnelStreamResponse>? tunnelResponseWriter;

    /// <summary>
    /// 用此来鉴定用户的 Hash
    /// </summary>
    private string? UserHashCode { get; set; }

    private Yue3.Model.OpenFrp.Response.Data.UserInfo? PrevUserInfo { get; set; }
    private AsyncLock prevUserInfoLock = new AsyncLock();

    internal Action<SyncWithSettingRequest> SyncSettingUpdate { get; set; } = delegate { };

    private static readonly BaseResponse @BaseResponse_NonAccess = new BaseResponse
    {
        Message = "无效的请求"
    };
    private static readonly BaseResponse @BaseResponse_NonAbleUserInfomationJson = new BaseResponse
    {
        Message = "无效的 UserInfomationJson"
    };
    private static readonly BaseResponse @BaseResponse_Success = new BaseResponse
    {
        Flag = true,
        Message = "操作成功"
    };

    private T? TryHandleExceptionOrContinue<T>(Task<T> task,T? defualtValue = default)
    {
        TryHandleExceptionOrContinue((Task)task);

        if (task.IsFaulted)
        {
            return defualtValue;
        }
        else
        {
            return task.Result;
        }
    }
    private void TryHandleExceptionOrContinue(Task task)
    {
        if (task.Exception is { InnerExceptions.Count: > 0 } or { InnerException: not null })
        {
            logger.LogError(task.Exception, "Daemon Handled Exception");
        }
        return;
    }

    public override async Task NotificationStream(Empty request, IServerStreamWriter<NotificationStreamResponse> responseStream, ServerCallContext context)
    {
        logger.LogInformation("[Notification Stream] Stream Created!");

        notificationResponseWriter = responseStream;

        await Task.Delay(-1, context.CancellationToken).ContinueWith(TryHandleExceptionOrContinue).ConfigureAwait(false);

        logger.LogInformation("[Notification Stream] Stream Finished!");

        notificationResponseWriter = default;

        return;
    }
    public bool IsClientConnected() => notificationResponseWriter is not null || tunnelResponseWriter is not null || logResponseWriter is not null;
    public bool HasUser() => PrevUserInfo is not null && !string.IsNullOrEmpty(UserHashCode);

    internal string? SetUser(Yue3.Model.OpenFrp.Response.Data.UserInfo uf_n)
    {
        var v = prevUserInfoLock.Lock();
        try
        {
            if (PrevUserInfo is not null && !uf_n.Equals(PrevUserInfo))
            {
                return default;
            }

            logger.LogInformation("[SetUser] Service Are trying: {uf_n}", System.Text.Json.JsonSerializer.Serialize(uf_n));

            UserHashCode = Helpers.HashAlgorithmHelper.ComputeHashString($"#{uf_n.UserID}={uf_n.RegisterTime},{uf_n.Email}");
            PrevUserInfo = uf_n;

            logger.LogInformation("[SetUser] Service Set User: {userHashCode}", UserHashCode);
        }
        finally
        {
            v.Dispose();
        }

        return UserHashCode;
    }

    public override Task<SyncResponse> Sync(Empty request, ServerCallContext context)
    {
        return Task.FromResult(new SyncResponse
        {
            IsLogon = PrevUserInfo is not null,
            CurrentId = PrevUserInfo?.UserID ?? default,
            Onlines =
            {
                processManager.GetOnlineProcesses()
            }
        });
    }

    public override async Task<BaseResponse> SyncWithLaunch(SyncWithLaunchRequest request, ServerCallContext context)
    {
        logger.LogDebug("[SyncWithLaunch] A: {A},Prev: {prev},Req Token:{token}", UserHashCode, PrevUserInfo,request.UserToken);
        if (!string.IsNullOrEmpty(UserHashCode))
        {
            string? hashCode = context.RequestHeaders.GetValue("HashCode");
            logger.LogDebug("[SyncWithLaunch] B: {B}", hashCode);
            if (string.IsNullOrEmpty(hashCode) || !hashCode!.Equals(UserHashCode))
            {
                return @BaseResponse_NonAccess;
            }
        }

        if (PrevUserInfo is not null && !string.IsNullOrEmpty(request.UserToken))
        {

            logger.LogDebug("[SyncWithLaunch] Length: {req}", request.RequireUserTunnels.Length);
            var lc = new Manager.Process.LaunchConfig(request.Config);

            lc.IsAutoLuanch = true;


            var buffers = new MemoryStream();
#if NET
            await buffers.WriteAsync(request.RequireUserTunnels.Memory);
#else
            byte[] mn1uhUH = request.RequireUserTunnels.ToByteArray();
            await buffers.WriteAsync(mn1uhUH, 0, mn1uhUH.Length);
#endif
            buffers.Seek(0, SeekOrigin.Begin);

            await foreach (Yue3.Model.OpenFrp.Response.Data.UserTunnel? tunnel in
                System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<Yue3.Model.OpenFrp.Response.Data.UserTunnel>(buffers))
            {
                if (tunnel is null) continue;

                logger.LogDebug("[SyncWithLaunch] App Launch Up: #{id} {ap}", tunnel.Id, tunnel.Name);

                object result;
                if (request.TomlConfigMap.Count > 0 && request.TomlConfigMap.TryGetValue(tunnel.Name,out string config))
                {
                    result = await processManager.LaunchTunnel(request.UserToken, config, tunnel, lc);
                }
                else
                {
                    result = processManager.LaunchTunnel(request.UserToken, tunnel, lc); 
                }

                logger.LogDebug("[SyncWithLaunch] Result: {resultType} App Launch Up: #{id} {ap}", result.GetType(), tunnel.Id, tunnel.Name);

                if (notificationResponseWriter is null || result is true) continue;

                NotificationStreamResponse message;
                switch (result)
                {
                    case Proto.Response.TunnelStreamResponse.Types.TunnelControlFailed tcf:
                        {
                            message = new NotificationStreamResponse
                            {
                                State = NotificationStreamResponse.Types.NotificationStreamResponseState.Messaging,
                                Data = Any.Pack(tcf)
                            };
                        }
                        ; break;
                    case Proto.Response.BaseResponse brp when !brp.Flag:
                        {
                            message = new NotificationStreamResponse
                            {
                                State = NotificationStreamResponse.Types.NotificationStreamResponseState.Messaging,
                                Data = Any.Pack(new NotificationStreamResponse.Types.UIWarningNotice
                                {
                                    Title = $"请注意隧道 {tunnel.Name}",
                                    Data = brp.Message
                                })
                            };
                        }
                        ; break;

                    default: continue;
                }
                await notificationResponseWriter.WriteAsync(message);
            }
            logger.LogDebug("[SyncWithLaunch] Finished Process.");
            return BaseResponse_Success;
        }
        return BaseResponse_NonAccess;
    }

    public override Task<BaseResponse> SyncWithSetting(SyncWithSettingRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(frpcManager.Feature.VersionString))
        {
            frpcManager.Feature.ForceUseConfig = request.UseConfigLaunch;

            SyncSettingUpdate.Invoke(request);
        }

        return Task.FromResult(new BaseResponse { Flag = true });
    }

    public override async Task<BaseResponse> Login(LoginRequest request, ServerCallContext context)
    {
        if (request.UserInfomationJson.Length is 0 || context.RequestHeaders.Get("Authorization") is not { Value: string authorization })
        {
            return @BaseResponse_NonAccess;
        }
        using var _pd = await prevUserInfoLock.LockAsync();
     
        var uf_n = System.Text.Json.JsonSerializer.Deserialize<Yue3.Model.OpenFrp.Response.Data.UserInfo>(request.UserInfomationJson.Span);

        logger.LogInformation("[Login] {uf_n}", System.Text.Json.JsonSerializer.Serialize(uf_n));

        if (uf_n is null || string.IsNullOrEmpty(uf_n.Email))
        {
            return @BaseResponse_NonAbleUserInfomationJson;
        }
        if (PrevUserInfo is not null && !uf_n.Equals(PrevUserInfo))
        {
            return new BaseResponse
            {
                Message = $"请登录到账户: {PrevUserInfo.Email}"
            };
        }

        logger.LogInformation("[Login] Set Authorization : {authorization}", authorization);

        Net.OpenFrpApi.SetAuthorization(authorization);

        // 每次登录都会刷新 

        UserHashCode = Helpers.HashAlgorithmHelper.ComputeHashString($"#{uf_n.UserID}={uf_n.RegisterTime},{uf_n.Email}");
        PrevUserInfo = uf_n;

        logger.LogInformation("[Login] {userHashCode}", UserHashCode);

        await context.WriteResponseHeadersAsync(new Metadata
        {
            { "HashCode",UserHashCode },
        });

        
        return @BaseResponse_Success;
    }

    public override async Task TunnelStream(IAsyncStreamReader<TunnelStreamRequest> requestStream, IServerStreamWriter<TunnelStreamResponse> responseStream, ServerCallContext context)
    {
        string? hashCode = context.RequestHeaders?.GetValue("HashCode");

        if (!string.IsNullOrEmpty(UserHashCode) && hashCode?.Equals(UserHashCode) is false)
        {
            await responseStream.WriteAsync(new TunnelStreamResponse
            {
                State = TunnelStreamResponse.Types.TunnelStreamResponseState.Messaging,
                Data = Any.Pack(BaseResponse_NonAccess)
            });
            return;
        }
        else
        {
            //anonymous tunnel stream
        }

        bool isAnonymousStream = string.IsNullOrEmpty(UserHashCode) || string.IsNullOrEmpty(hashCode);

        logger.LogInformation("[Tunnel Stream] Stream Created!");
        tunnelResponseWriter = responseStream;
        string? userToken = default;

        while (await requestStream.MoveNext(context.CancellationToken).ContinueWith((task) => TryHandleExceptionOrContinue(task)).ConfigureAwait(false)) 
        {
            var current = requestStream.Current;

            Manager.Process.LaunchConfig? tlConfig = default;
            Yue3.Model.OpenFrp.Response.Data.UserTunnel? userTunnel = default;

            // template token :: used by "fast launch"
            string? trmToken = default;
            string? tomlConfig = default;

            logger.LogInformation("[Tunnel Stream] State {state}, Data: {data}",current.State,current.Data?.TypeUrl ?? current.Data?.GetType().ToString());
            

            if (current.State is TunnelStreamRequest.Types.TunnelStreamRequestState.LaunchTunnel or TunnelStreamRequest.Types.TunnelStreamRequestState.CloseTunnel)
            {
                if (current.Data is null)
                {
                    continue;
                }
             
                if (current.Data.TryUnpack<TunnelStreamRequest.Types.TunnelLaunchReq>(out var launchReq) && (launchReq.OriginalJsonBuffer.Length > 0 || launchReq.HasOriginalFastLaunchCall))
                {
                    try
                    {
                        userTunnel = System.Text.Json.JsonSerializer.Deserialize<Yue3.Model.OpenFrp.Response.Data.UserTunnel>(launchReq.OriginalJsonBuffer.Span);
                    }
                    catch
                    {
                        
                    }
                    tlConfig = new(launchReq.Config) { IsFastLaunch = launchReq.TunnelSourceCase is TunnelStreamRequest.Types.TunnelLaunchReq.TunnelSourceOneofCase.OriginalFastLaunchCall };

                    switch (launchReq.TunnelSourceCase)
                    {
                        case TunnelStreamRequest.Types.TunnelLaunchReq.TunnelSourceOneofCase.OriginalFastLaunchCall when launchReq.HasOriginalFastLaunchCall:
                            {
                                try
                                {
                                    logger.LogDebug("[Tunnel Stream] Get UserTunnel Info by OriginalFastLaunchCall ({call})", launchReq.OriginalFastLaunchCall);

                                    var dit = Service.Net.HttpClient.ParseQueryString(launchReq.OriginalFastLaunchCall);

                                    if(dit.TryGetValue("proxy",out string? tidString) && int.TryParse(tidString,out var tid))
                                    {
                                        if (!dit.TryGetValue("user", out string? token) || !dit.TryGetValue("name", out string? name) || !dit.TryGetValue("remote", out string? remoteLink) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(remoteLink))
                                        {
                                            return;
                                        }
                                        trmToken = token;

                                        userTunnel = new Yue3.Model.OpenFrp.Response.Data.UserTunnel
                                        {
                                            ConnectAddress = remoteLink,
                                            Id = tid,
                                            Name = name,
                                        };
                                    }
                                }
                                catch
                                {
                                    goto default;
                                }
                            };break;
                        case TunnelStreamRequest.Types.TunnelLaunchReq.TunnelSourceOneofCase.OriginalTomlConfig when launchReq.HasOriginalTomlConfig:
                            {
                                try
                                {
                                    logger.LogDebug("[Tunnel Stream] Get UserTunnel Info by OriginalTomlConfig ({call})", launchReq.OriginalTomlConfig);

                                    if (userTunnel is not null)
                                    {
                                        tomlConfig = launchReq.OriginalTomlConfig;
                                    }
                                }
                                catch
                                {
                                    goto default;
                                }
                            };break;
                        default:
                            {
                                if (userTunnel is null || !launchReq.HasOriginalTomlConfig)
                                {
                                    await responseStream.WriteAsync(new TunnelStreamResponse
                                    {
                                        State = TunnelStreamResponse.Types.TunnelStreamResponseState.Messaging,
                                        Data = Any.Pack(new BaseResponse
                                        {
                                            Message = "Invaild Tunnel Data"
                                        })
                                    });
                                }
                            };break;

                    }
                    
                }
            }
            switch (current.State)
            {
                case TunnelStreamRequest.Types.TunnelStreamRequestState.Prepare:
                    {
                        logger.LogDebug("[Tunnel Stream] Prepare provided: {type}", current.Data?.TypeUrl);

                        if (current.Data is null || !current.Data.TryUnpack<StringValue>(out var svr) || string.IsNullOrEmpty(svr.Value))
                        {
                            if (!isAnonymousStream)
                            {
                                await responseStream.WriteAsync(new TunnelStreamResponse
                                {
                                    State = TunnelStreamResponse.Types.TunnelStreamResponseState.Messaging,
                                    Data = Any.Pack(@BaseResponse_NonAccess)
                                });
                                return;
                            }
                        }
                        else
                        {
                            if (svr.Value.Equals("testConnection"))
                            {
                                return;
                            }
                            userToken = svr.Value;
                        }
                        await responseStream.WriteAsync(new TunnelStreamResponse
                        {
                            State = TunnelStreamResponse.Types.TunnelStreamResponseState.Messaging,
                            Data = Any.Pack(new BaseResponse
                            {
                                Flag = true,
                                Message = "W&!AskForUrlScheme"
                            })
                        });
                        break;
                    };
                case TunnelStreamRequest.Types.TunnelStreamRequestState.GetOnlineTunnel:
                    {
                        await responseStream.WriteAsync(new TunnelStreamResponse
                        {
                            State = TunnelStreamResponse.Types.TunnelStreamResponseState.UpdateTunnel,
                            Data = Any.Pack(new Google.Protobuf.WellKnownTypes.ListValue
                            {
                                Values =
                                {
                                    processManager.GetOnlineProcesses().Select(x => new Value { NumberValue = x })
                                }
                            })
                        });

                        var len = processManager.GetContainers(x => x.Value.IsFastLaunch);
                        if (len.Length <= 0)
                        {
                            await responseStream.WriteAsync(new TunnelStreamResponse
                            {
                                State = TunnelStreamResponse.Types.TunnelStreamResponseState.UpdateTunnel,
                                Data = Any.Pack(new TunnelStreamResponse.Types.AnonymousTunnelResponse
                                {
                                    Datas = { }
                                })
                            });
                        }
                        else
                        {
                            await responseStream.WriteAsync(new TunnelStreamResponse
                            {
                                State = TunnelStreamResponse.Types.TunnelStreamResponseState.UpdateTunnel,
                                Data = Any.Pack(new TunnelStreamResponse.Types.AnonymousTunnelResponse
                                {
                                    Datas =
                                    {
                                        len.Select(ap =>
                                        {
                                            return new TunnelStreamResponse.Types.AnonymousTunnelResponse.Types.AnonymousTunnelData
                                            {
                                                Name = ap.UserTunnel.Name,
                                                ConnectAddresses = ap.UserTunnel.ConnectAddress,
                                                TunnelId = ap.UserTunnel.Id
                                            };
                                        })
                                    }
                                })
                            });
                        }
                        
                    }
                    ;break;
                case TunnelStreamRequest.Types.TunnelStreamRequestState.LaunchTunnel:
                    {
                        if (userTunnel is not null)
                        {
                            var val = await processManager.LaunchTunnel(
                                userToken ?? trmToken ?? string.Empty, tomlConfig, 
                                userTunnel, tlConfig ?? new Manager.Process.LaunchConfig { });

                            logger.LogDebug("[Tunnel Stream] Launch Result Type: {valType}", val?.GetType());

                            switch (val)
                            {
                                case true or false:
                                    {
                                        // merge by // manager.OnProcessStateChanged;
                                    };break;
                                case Proto.Response.BaseResponse brp:
                                    {
                                        await responseStream.WriteAsync(new TunnelStreamResponse
                                        {
                                            State = TunnelStreamResponse.Types.TunnelStreamResponseState.Messaging,
                                            Data = Any.Pack(brp)
                                        });
                                    };break;
                                case Proto.Response.TunnelStreamResponse.Types.TunnelControlFailed br2:
                                    {
                                        await responseStream.WriteAsync(new TunnelStreamResponse
                                        {
                                            State = TunnelStreamResponse.Types.TunnelStreamResponseState.Messaging,
                                            Data = Any.Pack(br2)
                                        });
                                    };break;
                                case TunnelStreamResponse.Types.AnonymousTunnelResponse trp:
                                    {
                                        await responseStream.WriteAsync(new TunnelStreamResponse
                                        {
                                            State = TunnelStreamResponse.Types.TunnelStreamResponseState.UpdateTunnel,
                                            Data = Any.Pack(trp)
                                        });
                                    }
                                    ;break;
                            }

                            
                        }
                        else
                        {
                            await responseStream.WriteAsync(new TunnelStreamResponse
                            {
                                State = TunnelStreamResponse.Types.TunnelStreamResponseState.Messaging,
                                Data = Any.Pack(new BaseResponse
                                {
                                    Message = "Please provide User's Tunnel in Data Property"
                                })
                            });
                        }
                        ;break;
                    }
                case TunnelStreamRequest.Types.TunnelStreamRequestState.CloseTunnel:
                    {
                        if (userTunnel is not null)
                        {
                            var flag = processManager.CloseTunnel(userTunnel);

                            if (flag is true)
                            {
                                // merge by // manager.OnProcessStateChanged
                                //await responseStream.WriteAsync(new TunnelStreamResponse
                                //{
                                //    State = TunnelStreamResponse.Types.TunnelStreamResponseState.UpdateTunnel,
                                //    Data = Any.Pack(new Int32Value { Value = -userTunnel.Id })
                                //});
                            }
                        }
                        else
                        {
                            await responseStream.WriteAsync(new TunnelStreamResponse
                            {
                                State = TunnelStreamResponse.Types.TunnelStreamResponseState.Messaging,
                                Data = Any.Pack(new BaseResponse
                                {
                                    Message = "Please provide User's Tunnel in Data Property"
                                })
                            });
                        }
                    ; break;
                    }
            }
        }

        tunnelResponseWriter = null;
        logger.LogInformation("[Tunnel Stream] Stream Finished!");
    }

    public override async Task LogStream(LogStreamRequest request, IServerStreamWriter<LogStreamResponse> responseStream, ServerCallContext context)
    {
        bool userLogon = false;
        if (!string.IsNullOrEmpty(UserHashCode))
        {
            string? hashCode = context.RequestHeaders?.GetValue("HashCode");
            if (!string.IsNullOrEmpty(hashCode) && hashCode!.Equals(UserHashCode))
            {
                userLogon = true;
            }
        }

        logger.LogInformation("[Log Stream] Stream Created! (UserLogon: {userLogin})", userLogon);

        await responseStream.WriteAsync(new LogStreamResponse
        {
            State = LogStreamResponse.Types.LogStreamResponseState.UpdateLinks,
            Data = Any.Pack(new LogStreamResponse.Types.LogsLiveData
            {
                Lives =
                {
                    rpcLogManager.GetAvaliableContainers().Select(x => new LogStreamResponse.Types.LogLive
                    {
                        Id = x.LogId,
                        Tag = x.Tag
                    })
                }
            })
        }).ContinueWith(TryHandleExceptionOrContinue).ConfigureAwait(false);

        if (request.IndexMaps.Count > 0)
        {
            logger.LogInformation("Rqeuest Log Mapping ::: ");
            foreach (var mp in request.IndexMaps)
            {
                logger.LogInformation("[ eg ::::::: {mp1} ::: {mp2} ]", mp.Key, mp.Value);

                if (rpcLogManager.Logs.TryGetValue(mp.Key, out var lc) && mp.Value > 0 && lc.Count > mp.Value)
                {
                    logger.LogInformation("| {key} | Client Has: {c} | Server Has: {s}", mp.Key, mp.Value, lc.Count);
                    await responseStream.WriteAsync(new LogStreamResponse
                    {
                        State = LogStreamResponse.Types.LogStreamResponseState.UpdateLogs,
                        Data = Any.Pack(new LogStreamResponse.Types.LogsData
                        {
                            Logs = { lc.Logs.Skip(mp.Value) },
                            LogId = mp.Key
                        })
                    }).ContinueWith(TryHandleExceptionOrContinue).ConfigureAwait(false);
                }
            }
            foreach (var v2 in rpcLogManager.Logs.Where(x => !request.IndexMaps.ContainsKey(x.Key)))
            {
                
                if (v2.Value.Logs is null || v2.Value.Logs.Count is 0) continue;

                logger.LogInformation("Update {tid} (not contain in Mapping)", v2.Key);

                await responseStream.WriteAsync(new LogStreamResponse
                {
                    State = LogStreamResponse.Types.LogStreamResponseState.UpdateLogs,
                    Data = Any.Pack(new LogStreamResponse.Types.LogsData
                    {
                        Logs = { v2.Value.Logs },
                        LogId = v2.Key
                    })
                }).ContinueWith(TryHandleExceptionOrContinue).ConfigureAwait(false);
            }
            logger.LogInformation("------------------------------------------------------");
        }
        else
        {
            foreach (var vex in rpcLogManager.Logs)
            {
                if (vex.Value.Logs is null) continue;

                await responseStream.WriteAsync(new LogStreamResponse
                {
                    State = LogStreamResponse.Types.LogStreamResponseState.UpdateLogs,
                    Data = Any.Pack(new LogStreamResponse.Types.LogsData
                    {
                        Logs = { vex.Value.Logs },
                        LogId = vex.Key
                    })
                }).ContinueWith(TryHandleExceptionOrContinue).ConfigureAwait(false);

            }
        }
        // 防抢
        this.logResponseWriter = responseStream;

        await Task.Delay(-1, context.CancellationToken).ContinueWith(TryHandleExceptionOrContinue).ConfigureAwait(false);

        logger.LogInformation("[Log Stream] Stream Finished!");

        this.logResponseWriter = null;
        // wait for logs
    }

    public override Task<BaseResponse> ClearLog(ClearLogRequest request, ServerCallContext context)
    {
        bool userLogon = false;
        if (!string.IsNullOrEmpty(UserHashCode))
        {
            string? hashCode = context.RequestHeaders.GetValue("HashCode");
            if (!string.IsNullOrEmpty(hashCode) && hashCode!.Equals(UserHashCode))
            {
                userLogon = true;
            }
        }

        logger.LogInformation("[Log Stream] Try Invoke \"Clear\" (UserLogon: {userLogin})", userLogon);

        if (request.LogId is -1)
        {
            foreach (var item in rpcLogManager.Logs.Values)
            {
                item.Logs.Clear();
            }
        }
        else if (rpcLogManager.Logs.TryGetValue(request.LogId,out var ct))
        {
            ct.Logs.Clear();
        }
        else
        {
            return Task.FromResult(BaseResponse_NonAccess);
        }

        return Task.FromResult(BaseResponse_Success);
    }

    public override Task<BaseResponse> Logout(Empty request, ServerCallContext context)
    {
        if (!string.IsNullOrEmpty(UserHashCode))
        {
            string? hashCode = context.RequestHeaders.GetValue("HashCode");
            if (string.IsNullOrEmpty(hashCode) || !hashCode!.Equals(UserHashCode))
            {
                return Task.FromResult(@BaseResponse_NonAccess);
            }
        }

        processManager.Shutdown();

        logger.LogInformation("[Logout] OK! Prev HashCode: {UserHashCode}", UserHashCode);
        
        Net.OpenFrpApi.SetAuthorization(default);
        UserHashCode = null;
        PrevUserInfo = null;

        return Task.FromResult(BaseResponse_Success);
    }
}