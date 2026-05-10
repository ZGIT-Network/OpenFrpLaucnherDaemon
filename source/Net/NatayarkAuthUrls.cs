using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFrp.Service.Net
{
    internal class NatayarkAuthUrls
    {
        /// <summary>
        /// 17A OAuth 平台
        /// </summary>
        public const string OAuthUrl = "https://account.naids.com";

        #region OAuth
        /// <summary>
        /// 获取对于平台的 AuthorizeCode ::: POST
        /// <para/>
        ///     登录到 OAuth 平台
        /// <para/>
        ///     内容: (POST Body)
        ///     {
        ///         "user": <see cref="string"/>
        ///         "password": <see cref="string"/>
        ///     }
        /// </summary>
        public const string OAuthLogin = $"{OAuthUrl}/api/public/login";

        /// <summary>
        /// 退出登录 ::: GET
        /// <para/>
        ///     退出登录
        /// </summary>
        public const string OAuthLogout = $"{OAuthUrl}/api/public/logout";
        #endregion
    }
}
