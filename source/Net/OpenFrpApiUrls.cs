using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFrp.Service.Net
{
    internal class OpenFrpApiUrls
    {

        /// <summary>
        /// OpenFRP API
        /// </summary>
        public const string OpenFrpApiUrl = "https://api.openfrp.net";

        public const string AccessOpenFrpApiUrl = "https://access.openfrp.net";

        public const string ZyghitApi = "https://api.zyghit.cn";

        public const string OpenFrpApiAuthoriozationHost = "api.openfrp.net";



        #region OpenFrp

        /// <summary>
        /// 登录到 OpenFRP 平台 ::: GET
        /// 
        /// <para/>
        ///     使用该链接来替换原有 /user/login 方式。
        /// <para/>
        ///     例子:
        ///     https://of-dev-api.bfsea.xyz/oauth2/callback?code=F9A1YGN9A1G3AA1C
        /// <para/>
        ///     URL Query: ?code=<see cref="string"/>
        /// </summary>
        public const string LoginOpenFrp = $"{OpenFrpApiUrl}/oauth2/callback";

        public const string GetAuthorizeUrl = $"{OpenFrpApiUrl}/oauth2/login";

        /// <summary>
        /// 获取 OpenFRP 用户数据 ::: POST
        /// 
        /// <para/>
        ///     获取用户在当前平台的数据
        /// </summary>
        public const string GetUserInfo = $"{OpenFrpApiUrl}/frp/api/getUserInfo";

        /// <summary>
        /// 获取 OpenFRP 用户隧道 ::: POST
        /// 
        /// <para/>
        ///     获取用户在当前平台的数据
        /// </summary>
        public const string GetUserTunnels = $"{OpenFrpApiUrl}/frp/api/getUserProxies";

        /// <summary>
        /// 移除用户在 OpenFRP 的指定隧道 ::: POST
        /// 
        /// <para/>
        ///     移除隧道。
        /// </summary>
        public const string RemoveTunnel = $"{OpenFrpApiUrl}/frp/api/removeProxy";

        /// <summary>
        /// 编辑用户在 OpenFRP 的指定隧道 ::: POST
        /// 
        /// <para/>
        ///     编辑隧道, 传入 Body 与 CreateTunnel 相同。
        /// </summary>
        public const string EditTunnel = $"{OpenFrpApiUrl}/frp/api/editProxy";

        /// <summary>
        /// 获取 OpenFRP 节点列表 ::: POST
        /// 
        /// <para/>
        ///     获取节点列表。
        /// </summary>
        public const string GetNodeList = $"{OpenFrpApiUrl}/frp/api/getNodeList";
        /// <summary>
        /// 创建新隧道 :: POST
        /// 
        /// <para/>
        ///     创建新的隧道。
        /// </summary>
        public const string CreateTunnel = $"{OpenFrpApiUrl}/frp/api/newProxy";

        /// <summary>
        /// 普通查询 :: GET
        /// 
        /// <para/>
        ///     通过指定 Key 来查询需要的项目
        /// </summary>
        public const string CommonQuery = $"{OpenFrpApiUrl}/commonQuery/get";

        /// <summary>
        /// 签到 :: POST
        /// 
        /// <para/>
        ///     通过输入 token 来完成签到
        /// </summary>
        public const string UserSignIn = $"{OpenFrpApiUrl}/frp/api/userSign";

        /// <summary>
        /// 获取签到状态 :: POST
        /// 
        /// <para/>
        ///     通过输入 token 来完成签到
        /// </summary>
        public const string GetUserSignInfo = $"{OpenFrpApiUrl}/frp/api/getSignInfo";
        /// <summary>
        /// 获取隧道状态 :: GET
        /// 
        /// <para/>
        ///     
        /// </summary>
        public const string GetNodeStatus = $"{OpenFrpApiUrl}/frp/api/getNodeStatus";

        /// <summary>
        /// 获取节点配置
        /// </summary>
        public const string GetNodeConfig = $"{OpenFrpApiUrl}/frp/api/getNodeConf";

        #endregion

        #region Access OpenFrp Api

        public const string AccessRequestLogin = $"{AccessOpenFrpApiUrl}/argoAccess/requestLogin";

        public const string AccessPollLogin = $"{AccessOpenFrpApiUrl}/argoAccess/pollLogin";
        #endregion

        #region Zyghit Api

        public const string LauncherAdSense = $"{ZyghitApi}/zg-adsense/openfrp-lanucher";

        #endregion

    }
}
