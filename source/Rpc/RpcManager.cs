using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Grpc.Core.Logging;
using OpenFrp.Service.Daemon;
using Nito.AsyncEx;


namespace OpenFrp.Service.Rpc
{
    public class RpcManager
    {
        internal string? PipeName;

        internal OpenFrp.Service.Proto.Service.OpenFrp.OpenFrpClient? OpenFrpDeamonRpcClient;

        private GrpcDotNetNamedPipes.NamedPipeChannel? Channel;

        internal readonly Metadata GlobalHeader = new Metadata { };

        public bool IsConfigured => OpenFrpDeamonRpcClient is not null || !string.IsNullOrEmpty(PipeName);

        public RpcManager(ILogger<RpcManager> logger)
        {
            this.logger = logger;
        }

        private readonly ILogger<RpcManager> logger;

        private AsyncLock rpcConfigurationLock = new AsyncLock { };

        public void Configure() => Configure(Daemon.Daemon.GetPipename());

        public void Configure(string pipeName)
        {
            if (IsConfigured) return;
            using (var @lock = rpcConfigurationLock.Lock())
            {
                PipeName = pipeName;

                Channel = new GrpcDotNetNamedPipes.NamedPipeChannel(".", pipeName, new GrpcDotNetNamedPipes.NamedPipeChannelOptions
                {
                    ConnectionTimeout = 10
                });

                OpenFrpDeamonRpcClient = new OpenFrp.Service.Proto.Service.OpenFrp.OpenFrpClient(Channel);
            }
        }


        public void Crack()
        {
            using (var @lock = rpcConfigurationLock.Lock())
            {
                PipeName = default;
                Channel = null;
                OpenFrpDeamonRpcClient = null;
            }
        }

        public async Task<OpenFrp.Service.Proto.RpcResponse<Service.Proto.Response.SyncResponse>> Sync(CancellationToken cancellationToken = default)
        {
            if (OpenFrpDeamonRpcClient is null)
            {
                throw new InvalidOperationException("RPC 客户端暂未配置。");
            }
            try
            {
                var resp = await Task.Run(async () => await OpenFrpDeamonRpcClient.SyncAsync(
                    /* deadline: Deadline, */
                    request: new Google.Protobuf.WellKnownTypes.Empty { },
                    cancellationToken: cancellationToken));

                if (resp is not null)
                {
                    return resp;
                }
            }
            catch(RpcException rpcEx)
            {
                if (rpcEx.StatusCode is StatusCode.Unavailable)
                {
                    return new InvalidOperationException("无法连接到 OpenFrp 守护进程(服务)，请确保守护进程已启动且正在运行。\n" +
                        $"DaemonFile: At {PipeName} Is {Service.Helpers.FileHelper.GetServiceExecutableFile()}", rpcEx);
                }
                return rpcEx;
            }
            catch(Exception ex)
            {
                return ex;
            }
            return Service.Proto.RpcResponse<Service.Proto.Response.SyncResponse>.FailedResponse;
        }

        public async Task<OpenFrp.Service.Proto.RpcResponse> SyncWithLaunch(Service.Proto.Request.SyncWithLaunchRequest request,CancellationToken cancellationToken = default)
        {
            if (OpenFrpDeamonRpcClient is null)
            {
                throw new InvalidOperationException("RPC 客户端暂未配置。");
            }
            try
            {
                var resp = await Task.Run(async () => await OpenFrpDeamonRpcClient.SyncWithLaunchAsync(
                    /* deadline: Deadline, */
                    request,
                    headers: GlobalHeader,
                    cancellationToken: cancellationToken));

                if (resp is not null)
                {
                    return resp;
                }
            }
            catch (RpcException rpcEx)
            {
                return rpcEx;
            }
            catch (Exception ex)
            {
                return ex;
            }
            return Service.Proto.RpcResponse<Service.Proto.Response.BaseResponse>.FailedResponse;
        }

        public async Task<OpenFrp.Service.Proto.RpcResponse> SyncWithSetting(Service.Proto.Request.SyncWithSettingRequest request, CancellationToken cancellationToken = default)
        {
            if (OpenFrpDeamonRpcClient is null)
            {
                throw new InvalidOperationException("RPC 客户端暂未配置。");
            }
            try
            {
                var resp = await Task.Run(async () => await OpenFrpDeamonRpcClient.SyncWithSettingAsync(
                    /* deadline: Deadline, */
                    request,
                    headers: GlobalHeader,
                    cancellationToken: cancellationToken));

                if (resp is not null)
                {
                    return resp;
                }
            }
            catch (RpcException rpcEx)
            {
                return rpcEx;
            }
            catch (Exception ex)
            {
                return ex;
            }
            return Service.Proto.RpcResponse<Service.Proto.Response.BaseResponse>.FailedResponse;
        }

        public async Task<OpenFrp.Service.Proto.RpcResponse> Login(Service.Proto.Request.LoginRequest request,CancellationToken cancellationToken = default)
        {
            if (OpenFrpDeamonRpcClient is null)
            {
                throw new InvalidOperationException("RPC 客户端暂未配置。");
            }
            try
            {
                GlobalHeader.Clear();

                if (Service.Net.OpenFrpApi.GetAuthorization() is not string { Length: > 0 } auth)
                {
                    throw new NullReferenceException(nameof(Service.Net.OpenFrpApi.GetAuthorization));
                }
                var resp = await Task.Run(() =>  OpenFrpDeamonRpcClient.LoginAsync(
                    /* deadline: Deadline, */
                    request: request,
                    headers: new Metadata
                    {
                        { "Authorization",auth }
                    },
                    cancellationToken: cancellationToken));

                var rev = await resp.ResponseAsync;

                if (rev.Flag)
                {
                    if (await resp.ResponseHeadersAsync is not { Count: > 0 } headers || headers.Get("HashCode") is not { Value: string hashCode })
                    {
                        throw new NullReferenceException(nameof(resp.ResponseHeadersAsync));
                    }
                    GlobalHeader.Add("HashCode", hashCode);
                }

                return rev;
            }
            catch (RpcException rpcEx)
            {
                return rpcEx;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public async Task<OpenFrp.Service.Proto.RpcResponse> Logout(CancellationToken cancellationToken = default)
        {
            if (OpenFrpDeamonRpcClient is null)
            {
                throw new InvalidOperationException("RPC 客户端暂未配置。");
            }
            if (GlobalHeader is null || GlobalHeader.Count < 1)
            {
                throw new ArgumentNullException(nameof(GlobalHeader));
            }
            try
            {
                var resp = await Task.Run(async () => await OpenFrpDeamonRpcClient.LogoutAsync(
                    request: new Google.Protobuf.WellKnownTypes.Empty { },
                    /* deadline: Deadline, */
                    headers: GlobalHeader,
                    cancellationToken: cancellationToken));

                if (resp.Flag)
                {
                    GlobalHeader.Clear();
                }

                return Service.Proto.RpcResponse.SuccessResponse;
            }
            catch (RpcException rpcEx)
            {
                return rpcEx;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public async Task<IDisposable> NotificationStream(
             Action<Service.Proto.Response.NotificationStreamResponse> readerCallback,
            CancellationToken cancellationToken = default)
        {
            if (OpenFrpDeamonRpcClient is null)
            {
                throw new InvalidOperationException("RPC 客户端暂未配置。");
            }

            var duplex = OpenFrpDeamonRpcClient.NotificationStream(
                request: new Empty { },
                headers: GlobalHeader,
                /* deadline: Deadline, */
                cancellationToken: cancellationToken);

            try
            {
                while (await duplex.ResponseStream.MoveNext(cancellationToken))
                {
                    readerCallback.Invoke(duplex.ResponseStream.Current);
                }
            }
            catch (Grpc.Core.RpcException e) when (e.StatusCode is StatusCode.Cancelled) { }
            catch (Grpc.Core.RpcException e) when (e.StatusCode is StatusCode.Unavailable && e.Status.Detail.Equals("failed to connect to all addresses"))
            {

            }

            return duplex;
        }

        public async Task<IDisposable> TunnelStream(
            string? userTokenAccess,
            Action<IClientStreamWriter<Service.Proto.Request.TunnelStreamRequest>> writerCallback,
            Action<Service.Proto.Response.TunnelStreamResponse> readerCallback,
            CancellationToken cancellationToken = default)
        {
            if (OpenFrpDeamonRpcClient is null)
            {
                throw new InvalidOperationException("RPC 客户端暂未配置。");
            }
            
            var duplex = OpenFrpDeamonRpcClient.TunnelStream(
                headers: GlobalHeader,
                /* deadline: Deadline, */
                cancellationToken: cancellationToken);

            writerCallback.Invoke(duplex.RequestStream);

            try
            {
                await duplex.RequestStream.WriteAsync(new Service.Proto.Request.TunnelStreamRequest
                {
                    State = Service.Proto.Request.TunnelStreamRequest.Types.TunnelStreamRequestState.Prepare,
                    Data = Any.Pack(new StringValue
                    {
                        Value = userTokenAccess ?? ""
                    })
                });
                while (await duplex.ResponseStream.MoveNext(cancellationToken))
                {
                    readerCallback.Invoke(duplex.ResponseStream.Current);
                }
            }
            catch (Grpc.Core.RpcException e) when (e.StatusCode is StatusCode.Cancelled) {  }
            catch (Grpc.Core.RpcException e) when (e.StatusCode is StatusCode.Unavailable && e.Status.Detail.Equals("failed to connect to all addresses"))
            {

            }
            catch (Grpc.Core.RpcException e) when (e.StatusCode is StatusCode.Unavailable && e.Status.Detail.Equals("Exception was thrown by handler."))
            {

            }

            return duplex;
        }

        public async Task<IDisposable> LogStream(
            Google.Protobuf.Collections.MapField<int, int> KnownLogIndexMapping,
            Action<Service.Proto.Response.LogStreamResponse> readerCallback,
            CancellationToken cancellationToken)
        {
            if (OpenFrpDeamonRpcClient is null)
            {
                throw new InvalidOperationException("RPC 客户端暂未配置。");
            }
            var duplex = OpenFrpDeamonRpcClient.LogStream(
                request: new Service.Proto.Request.LogStreamRequest { IndexMaps = { KnownLogIndexMapping } },
                headers: GlobalHeader,
                /* deadline: Deadline, */
                cancellationToken: cancellationToken);

            try
            {
                while (await duplex.ResponseStream.MoveNext(cancellationToken))
                {
                    readerCallback.Invoke(duplex.ResponseStream.Current);
                }
            }
            catch (Grpc.Core.RpcException e) when (e.StatusCode is StatusCode.Cancelled) {  }
            catch (Grpc.Core.RpcException e) when (e.StatusCode is StatusCode.Unavailable && e.Status.Detail.Equals("failed to connect to all addresses"))
            {

            }

            return duplex;
        }

        public async Task<OpenFrp.Service.Proto.RpcResponse> ClearLog(int id,CancellationToken cancellationToken = default)
        {
            if (OpenFrpDeamonRpcClient is null)
            {
                throw new InvalidOperationException("RPC 客户端暂未配置。");
            }
            try
            {
                var resp = await Task.Run(async () => await OpenFrpDeamonRpcClient.ClearLogAsync(
                    request: new Service.Proto.Request.ClearLogRequest { LogId = id },
                    /* deadline: Deadline, */
                    headers: GlobalHeader,
                    cancellationToken: cancellationToken));

                return resp;

            }
            catch (RpcException rpcEx)
            {
                return rpcEx;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
