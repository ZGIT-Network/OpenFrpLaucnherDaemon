using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenFrp.Service.Manager.Log
{
    public class LogManager
    {
        public LogManager()
        {
            Logs = new Dictionary<int, LogContainer>()
            {
                {
                    0,
                    new LogContainer("Daemon")
                    {

                    }
                }
            };
        }

        internal event EventHandler<Proto.Response.LogStreamResponse.Types.LogContainer> OnNewLogPosted = delegate { };

        internal Dictionary<int, LogContainer> Logs { get; private set; }

        public IEnumerable<(int LogId,string Tag)> GetAvaliableContainers()
        {
            return Logs.Select(x => (x.Key,x.Value.Tag));
        }


        public void WriteLog(int id,string tag,string message,Service.Proto.Response.LogStreamResponse.Types.LogContainer.Types.LogLevel level)
        {
            if (!Logs.TryGetValue(id, out LogContainer? value) || value is null)
            {
                value = new LogContainer(tag);

                Logs.Add(id, value);
            }

            var log = new Proto.Response.LogStreamResponse.Types.LogContainer
            {
                Data = message,
                Date = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.Now),
                Level = level,
                Tag = tag,
                LogId = id
            };
            value.Logs.Add(log);

            OnNewLogPosted.Invoke(this, log);

            //Logs[0].Logs.Add(log);
            
        }

        public void Remove(int id)
        {
            Logs.Remove(id);
        }
    }
}
