using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace OpenFrp.Service.Net
{
    public class NatayarkAuth
    {
        public static async Task<Yue3.Model.Result.HttpResponse> Login(string username, string password, CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.PostAsync<Yue3.Model.NatayarkAuth.Response.BaseResponse>(NatayarkAuthUrls.OAuthLogin, new Yue3.Model.NatayarkAuth.Request.LoginRequest()
            {
                Username = username,
                Password = password
            }, cancellationToken).TranslateModelResult();
        }
        public static async Task<Yue3.Model.Result.HttpResponse> Logout(CancellationToken cancellationToken = default)
        {
            return await HttpClient.DefualtInstance.GetAsync<Yue3.Model.NatayarkAuth.Response.BaseResponse>(NatayarkAuthUrls.OAuthLogout, cancellationToken).TranslateModelResult();
        }
    }
}
