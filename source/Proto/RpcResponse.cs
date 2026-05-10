using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;

namespace OpenFrp.Service.Proto
{
    public class RpcResponse : Yue3.Model.Result.Result
    {
        public Exception? Exception { get; set; }

        public Google.Rpc.Status? Status
        {
            get
            {
                if (Exception is RpcException x)
                {
                    return x.GetRpcStatus();
                }
                return default;
            }
        }

        private Grpc.Core.StatusCode _statusCode = Grpc.Core.StatusCode.Unknown;
        public Grpc.Core.StatusCode? StatusCode
        {
            get
            {
                if (Exception is RpcException x)
                {
                    return x.StatusCode;
                }
                return _statusCode;
            }
            protected set => _statusCode = value ?? Grpc.Core.StatusCode.Unknown;
        }

        public bool Flag { get; set; }

        public string? Message { get; set; }

        public static implicit operator RpcResponse(OpenFrp.Service.Proto.Response.BaseResponse response)
        {
            return new RpcResponse
            {
                Flag = response.Flag,
                Message = response.Message,
            };
        }

        public static implicit operator RpcResponse(Exception ex)
        {
            return new RpcResponse
            {
                Exception = ex
            };
        }

        public static readonly RpcResponse FailedResponse = new RpcResponse { };

        public static readonly RpcResponse SuccessResponse = new RpcResponse { Flag = true };
    }

    public class RpcResponse<TData> : RpcResponse
    {
        public TData? Data { get; set; }

        public static new readonly RpcResponse<TData> FailedResponse = new RpcResponse<TData> { };

        public static new readonly RpcResponse<TData> SuccessResponse = new RpcResponse<TData> { Flag = true };

        public static implicit operator RpcResponse<TData>(OpenFrp.Service.Proto.Response.BaseResponse response)
        {
            return new()
            {
                Flag = response.Flag,
                Message = response.Message,
            };
        }

        public static implicit operator RpcResponse<TData>(TData data)
        {
            return new RpcResponse<TData> { Data = data, Flag = true,StatusCode = Grpc.Core.StatusCode.OK };
        }

        public static implicit operator RpcResponse<TData>(Exception ex)
        {
            return new()
            {
                Exception = ex
            };
        }
    }
}
