using System.Text;

namespace OpenFrp.Service.Manager.Process
{
    public struct LaunchConfig
    {
        public LaunchConfig() { }

        public LaunchConfig(Proto.Request.TunnelStreamRequest.Types.TunnelLaunchConfig? conf)
        {
            if (conf is null) return;

            DisableColorConsole = conf.AllowDisableConsoleColor;
            UseDebug = conf.UseDebug;
            UseDoh = conf.UseDoh;
            this.ForceTlsEncrypt = conf.UseForceTls;
            DohSource = conf.DohSource;
        }

        public bool IsFastLaunch { get; set; } = false;

        public bool DisableColorConsole { get; set; } = false;

        public bool ForceTlsEncrypt { get; set; } = false;

        public bool UseDebug { get; set; } = false;

        public bool UseDoh { get; set; } = false;

        public string DohSource { get; set; } = "doh.pub";

        public bool IsAutoLuanch { get; set; } = false;

        public string? ConfigFilePath { get; private set; }

        public void SetConfigFilePath(string path) => ConfigFilePath = path;


        public readonly string GenerateArgument(string userToken,int tunnelId)
        {
            StringBuilder @string = new StringBuilder();
            @string.Append("-n ");
            if (string.IsNullOrEmpty(ConfigFilePath))
            {
                @string.Append($"-u {userToken} -p {tunnelId}");
            }
            else
            {
                @string.Append($"--config \"{ConfigFilePath}\"");
            }
            @string.Append(' ');
            if (UseDebug)
            {
                @string.Append("--debug");
            }
            @string.Append(' ');
            if (ForceTlsEncrypt)
            {
                @string.Append("--force_tls");
            }
            @string.Append(' ');
            if (DisableColorConsole)
            {
                @string.Append("--disable-log-color");
            }
            @string.Append(' ');
            if (UseDoh)
            {
                @string.Append("--use-doh");

                if (string.IsNullOrEmpty(DohSource))
                {
                    @string.Append(" --doh-addr=\"doh.pub\"");
                }
                else
                {
                    @string.Append($" --doh-addr=\"{DohSource}\"");
                }
                @string.Append(' ');
            }
            return @string.ToString().Trim();
        }
    }
}