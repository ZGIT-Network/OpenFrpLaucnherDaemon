using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFrp.Service.Manager.Frpc
{
    public class FrpcFeatrue
    {
        public string VersionString { get; set; } = "Unknown";

        public bool ForceUseConfig { get; set; }

        public bool AllowDisableConsoleColor { get; set; }
    }
}
