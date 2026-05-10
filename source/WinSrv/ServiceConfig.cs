using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OpenFrp.Service.WinSrv
{
    internal class ServiceConfig
    {
        [JsonPropertyName("userAutoLaunchTunnel")]
        public int[]? UserAutoLaunchTunnel { get; set; }

        [JsonPropertyName("useTomlConfig")]
        public bool UseTomlConfig { get; set; }

        [JsonPropertyName("useForceTls")]
        public bool UseForceTlsEncrypt { get; set; }

        [JsonPropertyName("useDebug")]
        public bool UseDebug { get; set; }

        [JsonPropertyName("useDoh")]
        public bool UseDoh { get; set; }

        [JsonPropertyName("user")]
        public string? UserAuthorization { get; set; }
    }
}
