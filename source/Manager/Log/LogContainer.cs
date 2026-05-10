using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFrp.Service.Manager.Log
{
    public class LogContainer
    {
        public LogContainer(string tag)
        {
            Logs = new HashSet<Proto.Response.LogStreamResponse.Types.LogContainer>();

            Tag = tag;
        }

        public int Count { get => Logs.Count; }

        public HashSet<Service.Proto.Response.LogStreamResponse.Types.LogContainer> Logs { get; private set; }

        public string Tag { get; internal set; }
    }
}
