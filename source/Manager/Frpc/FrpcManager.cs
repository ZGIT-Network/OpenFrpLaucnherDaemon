using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenFrp.Service.Helpers;


namespace OpenFrp.Service.Manager.Frpc
{
    public partial class FrpcManager
    {
        public FrpcFeatrue Feature { get; set; } = new FrpcFeatrue { };

        public string FrpcVersionString { get => Feature.VersionString; }

        public async Task<bool> DetectFrpcVersionAndFeatrue(string? fp = default)
        {
            try
            {
                if (fp is null)
                {
                    if (Helpers.FileHelper.GetFRPClient() is not { } path)
                    {
                        throw new NullReferenceException("未找到 FRPC 文件或访问拒绝。");
                    }
                    fp = path;
                }

                var pro = new System.Diagnostics.Process
                {
                    StartInfo =
                    {
                        CreateNoWindow = true,
                        FileName = fp,
                        UseShellExecute = false,
                        StandardOutputEncoding = Encoding.UTF8,
                        RedirectStandardOutput = true,
                        Arguments = "-v"
                    }
                    };
                if (await pro.StartAsync())
                {
                    await pro.WaitForExitAsync();

                    while (!pro.StandardOutput.EndOfStream)
                    {
                        string? str = await pro.StandardOutput.ReadLineAsync();

                        if (string.IsNullOrEmpty(str)) continue;
                        if (FrpcVersionRegex.Match(str) is { Groups.Count: > 0 } match)
                        {
                            if (match.Groups[match.Groups.Count - 2] is { Success: true, Value: string vlat } && int.TryParse(vlat, out var vlat_i))
                            {
                                if (vlat_i >= 60)
                                {
                                    Feature.AllowDisableConsoleColor = true;
                                }
                                else if (vlat_i < 60)
                                {
                                    Feature.ForceUseConfig = true;
                                }
                            }
                            Feature.VersionString = str;

                            return true;
                        }
                    }
                }
                else if (pro.HasExited)
                {
                    throw new System.ComponentModel.Win32Exception($"进程启动失败或无版本号输出: {pro.ExitCode}");
                }
            }
            catch(Win32Exception ex)
            {
                switch (ex.NativeErrorCode)
                {
                    case 5:
                        throw new Win32Exception(5, "无法启动 FRPC 进程，是否被杀软拒绝？"); 
                    case 225 or 226:
                        throw new Win32Exception(5, $"操作被拒绝，可能被杀软拦截。({ex.Message})");
                    default:
                        throw;
                }
                
            }
            catch (Exception)
            {
                throw;
            }
            return false;
        }

        private readonly Regex FrpcVersionRegex = FrpcVersionRegexFun();

#if NET
        [GeneratedRegex("O(\\D*?)F(\\D*?)_(\\d{0,2}).(\\d{0,3}).(\\d{0,2})")]
        private static partial Regex FrpcVersionRegexFun();
#else
        private static Regex FrpcVersionRegexFun() => new Regex("O(\\D*?)F(\\D*?)_(\\d{0,2}).(\\d{0,3}).(\\d{0,2})");
#endif


    }
}
