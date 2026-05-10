using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;


namespace OpenFrp.Service.WebApp
{
    public class WebApp
    {
        internal static Task ExecuteAsync(ParseResult result)
        {
            var host = WebHost.CreateDefaultBuilder();

            

            host.UseStartup<Startup>()
                .UseKestrel(x =>
                {
                    x.ListenLocalhost(8087,option =>
                    {

                    });
                });

            var app = host.Build();


            return app.RunAsync();
        }

        private class Startup : IStartup
        {
            public void Configure(IApplicationBuilder app)
            {
                app.UseMvc();
                app.UseWelcomePage("/");
            }

            public IServiceProvider ConfigureServices(IServiceCollection services)
            {
                
                services.AddMvc();
                services.AddHostedService<ChallengeService>();
                //services.AddHttpClient();
                services.AddSingleton<Rpc.DaemonManager>();
                services.AddSingleton<Rpc.RpcManager>();
                services.AddSingleton<Manager.Frpc.FrpcManager>();
                services.AddSingleton<PanelAuthorizationHelper>();

                return services.BuildServiceProvider();
            }
        }
    }

    public class PanelAuthorizationHelper
    {
        public PanelAuthorizationHelper()
        {
            aes.GenerateIV();
            aes.GenerateKey();

            md5.Key = aes.Key;
        }

        public ConcurrentDictionary<string, DateTimeOffset> Challenges { get; protected set; } = new ConcurrentDictionary<string, DateTimeOffset> { };

        public string GenerateChallenge(string salt)
        {
            return GetMD5String($"challenge+{salt}");
        }

        public string CalculatorPassword(string salt,string key)
        {
            using HMACSHA256 md25 = new HMACSHA256()
            {
                Key = Encoding.UTF8.GetBytes(key)
            };
            

            byte[] buffer = Encoding.UTF8.GetBytes(salt);

            byte[] hash = md25.ComputeHash(buffer);

            

            return Convert.ToBase64String(hash);
        }

        public string GetMD5String(string value)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(value);

            byte[] hash = md5.ComputeHash(buffer);

            StringBuilder builder = new StringBuilder();

            foreach (var cha in hash)
            {
                builder.Append(cha.ToString("x2"));
            }

            return builder.ToString();
        }

        public string GenerateAuthorization(string salt)
        {
            byte[] encryptedBytes;

            var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    var plainBytes = Encoding.UTF8.GetBytes(salt);
                    cs.Write(plainBytes, 0, plainBytes.Length);
                    cs.FlushFinalBlock();
                    encryptedBytes = ms.ToArray();
                }
            }

            return Convert.ToBase64String(encryptedBytes);
        }

        public string? GetAuthorizationValue(string base64)
        {
            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(base64);

                var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                using (var ms = new MemoryStream(encryptedBytes))
                {
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    {
                        using (var sr = new StreamReader(cs, Encoding.UTF8))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
            }
            catch(Exception)
            {

            }
            return default;
        }

        private readonly HMACMD5 md5 = new HMACMD5();
        private readonly Aes aes = Aes.Create();
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class PanelAuthorization : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // 1. 检查用户是否已认证
            var user = context.HttpContext.Request.Headers["Authorization"];

            if (user.Count == 0 || user.SingleOrDefault() is not string token)
            {
                context.Result = new ObjectResult(new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = "凭证无效，请重新登录"
                })
                {
                    StatusCode = 401
                };
                return;
            }

            var authHelper = context.HttpContext.RequestServices.GetRequiredService<PanelAuthorizationHelper>();

            string? fut = authHelper.GetAuthorizationValue(token);

            if (!string.IsNullOrEmpty(fut) && fut.Contains('|'))
            {
                string[] futs = fut!.Split('|');

                if (futs.Length == 2 && long.TryParse(futs[0], out long dat))
                {
                    var p = DateTimeOffset.FromUnixTimeMilliseconds(dat) - DateTimeOffset.Now;

                    if (p.TotalHours < 48)
                    {
                        if (p.TotalHours > 40)
                        {
                            context.HttpContext.Response.Headers.Add("Authorization", authHelper.GenerateAuthorization($"{DateTimeOffset.Now.ToUnixTimeMilliseconds()}|{futs[1]}"));
                        }
                        return;
                    }
                }
            }
            else
            {
                context.Result = new ObjectResult(new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = "凭证无效，请重新登录"
                })
                {
                    StatusCode = 401
                };
                return;
            }
        }
    }

    public class ChallengeService : BackgroundService
    {
        private readonly PanelAuthorizationHelper authorizationHelper;
        private readonly Rpc.RpcManager rpcManager;
        private readonly Rpc.DaemonManager daemonManager;

        public ChallengeService(PanelAuthorizationHelper authorizationHelper, Rpc.RpcManager rpcManager,
            Rpc.DaemonManager daemonManager)
        {
            this.authorizationHelper = authorizationHelper;
            this.rpcManager = rpcManager;
            this.daemonManager = daemonManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000 * 60, stoppingToken);

                foreach (var kv in authorizationHelper.Challenges)
                {
                    if ((kv.Value - DateTimeOffset.Now).TotalSeconds > 60)
                    {
#if NETFRAMEWORK
                        authorizationHelper.Challenges.TryRemove(kv.Key, out _);
#else
                        authorizationHelper.Challenges.TryRemove(kv);
#endif
                    }
                }
            }
        }
    }

    [ApiController()]
    [Route("api")]
    public class ApiController : Controller
    {   
        public ApiController(Manager.Frpc.FrpcManager frpcManager,
            PanelAuthorizationHelper authorizationHelper,
            ILogger<ApiController> logger,
            Rpc.RpcManager rpcManager,
            Rpc.DaemonManager daemonManager)
        {
   
            this.frpcManager = frpcManager;
            this.authorizationHelper = authorizationHelper;
            this.logger = logger;
            this.rpcManager = rpcManager;
            this.daemonManager = daemonManager;
        }

        private readonly ILogger<ApiController> logger;
        private readonly Manager.Frpc.FrpcManager frpcManager;
        private readonly Rpc.RpcManager rpcManager;
        private readonly Rpc.DaemonManager daemonManager;
        private readonly PanelAuthorizationHelper authorizationHelper;

        private Task<bool> frpcManagerDelay = Task.FromResult(true);

        [PanelAuthorization]
        [HttpGet("openfrp/commonQuery")]
        public async Task<IActionResult> GetBroadcast([FromQuery] string key)
        {
            var resp = await Net.OpenFrpApi.CommonQueryGet<JsonNode>(key);

            if (resp.Data != null)
            {
                var str = System.Text.Json.JsonSerializer.Serialize(new Yue3.Model.WebPanel.Response.BaseResponse<JsonNode>()
                {
                    Data = resp.Data,
                    Message = resp.Message
                });
                return Ok(str);
            }
            else
            {
                return StatusCode((int)resp.StatusCode,resp.Exception?.Message ?? resp.Message);
            }
        }

        [PanelAuthorization]
        [HttpGet("openfrp/userInfo")]
        public async Task<IActionResult> GetUserInfo()
        {
            var resp = await Net.OpenFrpApi.GetUserInfo();

            if (resp.Data != null)
            {
                return Ok(new Yue3.Model.WebPanel.Response.BaseResponse<Yue3.Model.OpenFrp.Response.Data.UserInfoData>
                {
                    Data = resp.Data
                });
            }
            else
            {
                return StatusCode((int)resp.StatusCode, resp.Exception?.Message ?? resp.Message);
            }
        }

        [PanelAuthorization]
        [HttpGet("openfrp/logout")]
        public void LogoutOpenFrp()
        {
            OpenFrp.Service.Net.OpenFrpApi.Logout();
        }

        [PanelAuthorization]
        [HttpGet("oauth/login")]
        public async Task<IActionResult> OAuthLogin([FromQuery] int port)
        {
            var resp = await OpenFrp.Service.Net.OpenFrpApi.GetAuthorizeUrl(port);

            if (resp.Data != null)
            {
                return Ok(new Yue3.Model.WebPanel.Response.BaseResponse<string>()
                {
                    Data = resp.Data,
                    Message = resp.Message
                });
            }
            else
            {
                return StatusCode((int)resp.StatusCode, resp.Exception?.Message ?? resp.Message);
            }
        }

        [PanelAuthorization]
        [HttpPost("oauth/callback")]
        public async Task<IActionResult> OAuthCallback([FromBody] Yue3.Model.WebPanel.Request.OAuthCallbackRequest request)
        {
            if (!request.IsVaild())
            {
                return BadRequest(new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = "无效的 Request"
                });
            }

            var resp = await OpenFrp.Service.Net.OpenFrpApi.Login(request.Code, request.RedirectUrl);

            if (resp.StatusCode != HttpStatusCode.OK)
            {
                return StatusCode((int)resp.StatusCode, resp.Exception?.Message ?? resp.Message);
            }

            return Ok(new Yue3.Model.WebPanel.Response.BaseResponse { });
        }



        [PanelAuthorization]
        [HttpGet("configuration")]
        public IActionResult GetUserConfiguration()
        {
            return Ok(new Yue3.Model.WebPanel.Response.BaseResponse<Yue3.Model.WebPanel.Response.Data.ConfigurationData>
            {
                Data = new Yue3.Model.WebPanel.Response.Data.ConfigurationData
                {
                    IsUserLogon = !string.IsNullOrEmpty(Net.OpenFrpApi.GetAuthorization()),
                    IsDaemonAlive = rpcManager.IsConfigured
                }
            });
        }

        [PanelAuthorization]
        [HttpGet("frpcVersion")]
        public async Task<IActionResult> GetFrpcVersion()
        {
            try
            {
                if (Service.Helpers.FileHelper.TryGetFRPClient(out string tp))
                {
                    frpcManagerDelay = frpcManager.DetectFrpcVersionAndFeatrue(tp);
                }
                else
                {
                    throw new FileNotFoundException(tp);
                }

                if (!await frpcManagerDelay.ConfigureAwait(false))
                {
                    return BadRequest(new Yue3.Model.WebPanel.Response.BaseResponse
                    {
                        Message = "无法检测到 FRPC 版本或特性。"
                    });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = ex.Message
                });
            }

           
            return Ok(new Yue3.Model.WebPanel.Response.BaseResponse<string>
            {
                Data = frpcManager.FrpcVersionString
            });
        }

        [HttpGet("challenge")]
        public async Task<IActionResult> GetChallenge([FromQuery] string salt)
        {
            if (string.IsNullOrEmpty(salt))
            {
                return BadRequest(new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = "salt cannot be empty or null"
                });
            }
            await Task.Yield();

            string hash = authorizationHelper.GenerateChallenge(salt);

            if (authorizationHelper.Challenges.TryGetValue(hash,out var v))
            {
                if ((v - DateTimeOffset.Now).TotalSeconds > 60)
                {
                    authorizationHelper.Challenges.TryUpdate(hash, DateTimeOffset.Now, v);

                    return Ok(new Yue3.Model.WebPanel.Response.BaseResponse<string>
                    {
                        Data = hash
                    });
                }
                else
                {
                    return BadRequest(new Yue3.Model.WebPanel.Response.BaseResponse
                    {
                        Message = "salt is used"
                    });
                }
            }

            authorizationHelper.Challenges.TryAdd(hash,DateTimeOffset.Now);

            return Ok(new Yue3.Model.WebPanel.Response.BaseResponse<string>
            {
                Data = hash
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] Yue3.Model.WebPanel.Request.LoginRequset request)
        {
            if (!request.IsVaild())
            {
                return BadRequest(new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = "无效的 Request"
                });
            }

            string ot = authorizationHelper.GenerateChallenge(request.Salt);

            if (!ot.Equals(request.Challenge))
            {
                return BadRequest(new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = "无效的 Challenge Code"
                });
            }

            authorizationHelper.Challenges.TryRemove(request.Challenge, out _);

            string opw = authorizationHelper.CalculatorPassword("255555", request.Challenge);

            if (!opw.Equals(request.Password))
            {
                return BadRequest(new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = "密码错误!"
                });
            }

            try
            {

                var pipeName = OpenFrp.Service.Daemon.Daemon.GetPipename();

                var mutex = new Mutex(true, $"service.{pipeName}", out var createdNewFlag);

                // 幽灵端口难以解决
                // 只能脱离子进程运行

                if (createdNewFlag || mutex.SafeWaitHandle.IsClosed)
                {
                    mutex.Close();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd",
                        Arguments = $"/c start \"\" /b \"{OpenFrp.Service.Helpers.FileHelper.GetServiceExecutableFile()}\" --daemon",
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    });
                    await Task.Delay(1500);
                }

                mutex.Close();

                await daemonManager.LaunchDaemonAsync();

                rpcManager.Configure();

                OpenFrp.Service.Proto.RpcResponse? resp = default;

                for (global::System.Int32 i = 0; i < 5; i++)
                {
                    resp = await rpcManager.Sync();

                    //logger.LogWarning("{resp}", System.Text.Json.JsonSerializer.Serialize(resp));

                    if (resp.StatusCode is Grpc.Core.StatusCode.OK)
                    {
                        break;
                    }
                    await Task.Delay(1000);
                }
                
                if (resp is null || resp.StatusCode != Grpc.Core.StatusCode.OK || !resp.Flag)
                {
                    return StatusCode(502, new Yue3.Model.WebPanel.Response.BaseResponse
                    {
                        Message = resp?.Message ?? resp?.Exception?.Message ?? "Unknown exception"
                    });
                }

                logger.LogInformation("{resp}", System.Text.Json.JsonSerializer.Serialize(new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = resp?.Message ?? resp?.Exception?.Message ?? "Unknown exception"
                }));
            }
            catch(Exception ex)
            {
                return StatusCode(502,new Yue3.Model.WebPanel.Response.BaseResponse
                {
                    Message = ex.Message
                });
            }

            Response.Headers["Authorization"] = authorizationHelper.GenerateAuthorization($"{DateTimeOffset.Now.ToUnixTimeMilliseconds()}|{request.Password}");

            return Ok(new Yue3.Model.WebPanel.Response.BaseResponse
            {
                Message = "登录成功"
            });
        }
    }
}
