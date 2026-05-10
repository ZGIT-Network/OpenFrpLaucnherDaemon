using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.Collections;

namespace OpenFrp.Service.Helpers
{
    public class FileHelper
    {
        public static string BaseDirectory { get; } = AppContext.BaseDirectory;

        public static string FrpcDirectory { get; } = Path.Combine(BaseDirectory, "frpc");

        public static string UserPlatform { get; } = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.X86 => "386",
            Architecture.Arm64 => "arm64",
            _ => throw new NotSupportedException("本软件暂不支持 ARMv7 等其他平台。"),
        };

        public static string GetFrpcWorkDictionary(string kid)
        {
            string pat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"OpenFrpLauncher","frpc",kid);

            return Directory.CreateDirectory(pat).FullName;
        }

        public static string GetTemplateFolder(string kid)
        {
            string pat = Path.Combine(Path.GetTempPath(), kid);
            return Directory.CreateDirectory(pat).FullName;
        }

        public static string? GetFRPClient()
        {
            try
            {
                var vf = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(FrpcDirectory);
                var prefix = "";

                if (Environment.OSVersion.Version.Major is 6 && Environment.OSVersion.Version.Minor < 2)
                {
                    prefix = "legacy_";
                }

                if (vf.GetFileInfo($"{prefix}frpc_windows_{UserPlatform}.exe") is { PhysicalPath: not null or "" } file)
                {
                    if (file.Exists)
                    {
                        return file.PhysicalPath;
                    }
                }
            }
            catch (System.IO.DirectoryNotFoundException)
            {
                try { System.IO.Directory.CreateDirectory(FrpcDirectory); } catch { }
            }
            return default;
        }

        public static bool TryGetFRPClient(out string path)
        {
            try
            {
                var vf = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(FrpcDirectory);
                var prefix = "";

                if (Environment.OSVersion.Version.Major is 6 && Environment.OSVersion.Version.Minor < 2)
                {
                    prefix = "legacy_";
                }

                if (vf.GetFileInfo($"{prefix}frpc_windows_{UserPlatform}.exe") is { PhysicalPath: not null or "" } file)
                {
                    path = file.PhysicalPath;

                    return file.Exists;
                }
            }
            catch
            {
                
            }

            path = FrpcDirectory;

            return false;
        }

        public static bool TryGetServiceConfigFile(string pi,out string path)
        {
            if (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) is { } appDataPath)
            {
                string applicationDataPath = Path.Combine(appDataPath, "OpenFrpLauncher");

                if (Directory.CreateDirectory(applicationDataPath) is not { } dir)
                {
                    path = "无法创建应用程序数据目录。";

                    return false;
                }

                var acl = dir.GetAccessControl(AccessControlSections.Access);
                acl.SetAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), FileSystemRights.FullControl, InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));

                dir.SetAccessControl(acl);

                path = Path.Combine(appDataPath, "OpenFrpLauncher", $"{pi}.service.json");
                return true;
            }
            else
            {
                path = "无法获取应用程序数据目录。";
            }
            return false;
        }

        public static string GetAutoStartupFile()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"OpenFrp Launcher.lnk");
        }

        public static string GetServiceExecutableFile()
        {
            try
            {
                
                if (Type.GetType("OpenFrp.Service.Helpers.FileHelper") is { Assembly.Location: var asmFully } && !string.IsNullOrEmpty(asmFully))
                {
#if NET
                    return asmFully.Substring(0, asmFully.Length - 4) + ".exe";
#else
                    return asmFully;
#endif
                }
                else if (System.Reflection.Assembly.GetExecutingAssembly() is { Location: var asmFully2 } && !string.IsNullOrEmpty(asmFully2))
                {
#if NET
                    return asmFully2.Substring(0, asmFully2.Length - 4) + ".exe";
#else
                    return asmFully2;
#endif
                }
                else
                {
                    string bt = Path.Combine(AppContext.BaseDirectory,"OpenFrp.Service.exe");

                    if (File.Exists(bt))
                    {
                        return bt;
                    }
                }
            }
            catch
            {

            }
            throw new NotSupportedException();
        }
    }
}
