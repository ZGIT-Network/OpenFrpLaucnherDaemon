using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Rpc;

namespace OpenFrp.Service.Net
{
    public class OpenFrpApi
    {
        public static string? GetAuthorization()
        {
            return HttpClient.DefualtInstance.GetAuthorization(OpenFrpApiUrls.OpenFrpApiAuthoriozationHost);
        }
        public static void SetAuthorization(string? code)
        {
            HttpClient.DefualtInstance.SetAuthorityAndAuthorization(OpenFrpApiUrls.OpenFrpApiAuthoriozationHost, code);
        }
        



        internal static string? UserSession { get; set; }

        /// <summary>
        /// 获取 OAuth 授权用 Url (Natayark OpenID)
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<string>> GetAuthorizeUrl(int port,CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.GetAsync<Yue3.Model.OpenFrp.Response.BaseResponse<string>>(OpenFrpApiUrls.GetAuthorizeUrl,new Dictionary<string,string>()
            {
                {"redirect_url",$"http://localhost:{port}/oauth_callback" }
            }, cancellationToken);
        }

        /// <summary>
        /// 登录平台
        /// </summary>
        /// <param name="apiUrl">根据 <see cref="GetAuthorizeUrl(CancellationToken)"/> 获得的 URL 地址，用于获取授权用 Code</param>
        static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.NatayarkAuth.Response.Data.Authorization>> Authorize(string apiUrl,CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.GetAsync<Yue3.Model.NatayarkAuth.Response.BaseResponse<Yue3.Model.NatayarkAuth.Response.Data.Authorization>>(apiUrl, cancellationToken);
        }


        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="code">从 OpenID 授权获得的 Code</param>
        /// <param name="redirect_url">先前的重定向 URL</param>
        public static async Task<Yue3.Model.Result.HttpResponse> Login(string code,string? redirect_url = default, CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> kv = new Dictionary<string, string>
            {
                { "code", code },
                { "redirect_url", redirect_url ?? string.Empty }
            };
            if (string.IsNullOrEmpty(redirect_url))
            {
                kv.Remove("redirect_url");
            }
            Yue3.Model.Result.HttpResponse<string> resp = await HttpClient.DefualtInstance.GetAsync<Yue3.Model.OpenFrp.Response.BaseResponse<string>>(OpenFrpApiUrls.LoginOpenFrp, kv, cancellationToken);

            if (!string.IsNullOrEmpty(resp.Data))
            {
                UserSession = resp.Data;
            }
            else if (string.IsNullOrEmpty(resp.Message))
            {
                resp.Message = "未知错误";
            }
            return resp;
        }

        /// <summary>
        /// 获取用户的个人数据
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.Data.UserInfoData>> GetUserInfo(CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.PostAsync<Yue3.Model.OpenFrp.Response.BaseResponse<Yue3.Model.OpenFrp.Response.Data.UserInfoData>>(OpenFrpApiUrls.GetUserInfo,default,cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 获取用户的隧道
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.Data.UserTunnelData>> GetUserTunnels(CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.PostAsync<Yue3.Model.OpenFrp.Response.BaseResponse<Yue3.Model.OpenFrp.Response.Data.UserTunnelData>>(OpenFrpApiUrls.GetUserTunnels,null, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 获取平台当前的所有节点
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.Data.NodeData>> GetNodes(CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.PostAsync<Yue3.Model.OpenFrp.Response.BaseResponse<Yue3.Model.OpenFrp.Response.Data.NodeData>>(OpenFrpApiUrls.GetNodeList,null , cancellationToken: cancellationToken);
        }

        /// <summary>
        /// (Access Auth Login) 提交授权登录请求
        /// </summary>
        /// <param name="publicKey"></param>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.Data.AccessWeb2Data>> AccessRequestLogin(string publicKey,CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.PostAsync<Yue3.Model.OpenFrp.Response.BaseResponse2<Yue3.Model.OpenFrp.Response.Data.AccessWeb2Data>>(OpenFrpApiUrls.AccessRequestLogin,new Yue3.Model.OpenFrp.Request.AccessLoginRequest
            {
                PublicKey = publicKey
            }, cancellationToken);
        }

        /// <summary>
        /// (Access Auth Login) UUID 提交取可用凭证
        /// </summary>
        /// <param name="request_guid"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.Data.AccessWeb2Data>> AccessPollLogin(string request_guid,CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.GetAsync<Yue3.Model.OpenFrp.Response.BaseResponse2<Yue3.Model.OpenFrp.Response.Data.AccessWeb2Data>>(OpenFrpApiUrls.AccessPollLogin, new Dictionary<string, string>()
            {
                {"request_uuid",request_guid }
            }, cancellationToken);
        }

        /// <summary>
        /// KV 查询
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<TData>> CommonQueryGet<TData>(string key,CancellationToken cancellationToken = default) where TData: class
        {
            return await HttpClient.DefualtInstance.GetAsync<Yue3.Model.OpenFrp.Response.BaseResponse<TData>>(OpenFrpApiUrls.CommonQuery,new Dictionary<string, string>()
            {
                { "key",key }
            }, cancellationToken);
        }


        /// <summary>
        /// 获取软件信息数据
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.Data.SoftWareVersionData>> GetSoftwareConfig(CancellationToken cancellationToken = default)
        {
            return await CommonQueryGet<Yue3.Model.OpenFrp.Response.Data.SoftWareVersionData>("software", cancellationToken);
        }

        /// <summary>
        /// 移除隧道
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.BaseResponse>> RemoveTunnel(int tunnelId,CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.PostAsync<Yue3.Model.OpenFrp.Response.BaseResponse>(OpenFrpApiUrls.RemoveTunnel, new Yue3.Model.OpenFrp.Request.RemoveTunnelRequest
            {
                Id = tunnelId
            },cancellationToken);
        }

        /// <summary>
        /// 创建隧道
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.BaseResponse>> CreateTunnel(Yue3.Model.OpenFrp.Request.ModifyTunnelRequest request,CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.PostAsync<Yue3.Model.OpenFrp.Response.BaseResponse>(OpenFrpApiUrls.CreateTunnel, request, cancellationToken);
        }

        /// <summary>
        /// 编辑隧道
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.BaseResponse>> EditTunnel(Yue3.Model.OpenFrp.Request.ModifyTunnelRequest request, CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.PostAsync<Yue3.Model.OpenFrp.Response.BaseResponse>(OpenFrpApiUrls.EditTunnel, request, cancellationToken);
        }

        /// <summary>
        /// 获取主页广告 AdSense
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<Yue3.Model.OpenFrp.Response.Data.AdSense[]>> GetLauncherAdSense(CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.GetAsync<Yue3.Model.OpenFrp.Response.BaseResponse2<Yue3.Model.OpenFrp.Response.Data.AdSense[]>>(OpenFrpApiUrls.LauncherAdSense, cancellationToken);
        }

        /// <summary>
        /// 获取节点配置
        /// </summary>
        public static async Task<Yue3.Model.Result.HttpResponse<string>> GetNodeConfig(int nodeId,CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.PostAsync<Yue3.Model.OpenFrp.Response.BaseResponse<string>>(OpenFrpApiUrls.GetNodeConfig, new Yue3.Model.OpenFrp.Request.NodeConfigGetRequest
            {
                NodeId = nodeId
            }, cancellationToken);
        }

        public static void Logout()
        {
            UserSession = null;
            HttpClient.DefualtInstance.RemoveAuthroization("api.openfrp.net");
        }
    }
}
