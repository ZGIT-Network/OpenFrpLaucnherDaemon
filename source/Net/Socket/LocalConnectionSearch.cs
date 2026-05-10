using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OpenFrp.Service.Net
{
    public static class LocalConnectionSearch
    {
        private static readonly string[] TypeLimited = new string[]
        {
            "TCP",
            "UDP"
        };

        public static async Task<ConcurrentBag<LocalConnection>> SearchConnection(CancellationToken cancellationToken = default)
        {
            ConcurrentDictionary<uint, string> Pnkv = new ConcurrentDictionary<uint, string>();
            ConcurrentBag<LocalConnection> connections = new ConcurrentBag<LocalConnection>();
#if NET
            await Parallel.ForEachAsync(Process.GetProcesses(), cancellationToken, async (process, token) =>
            {
                await Task.Yield();
#else
            Parallel.ForEach(Process.GetProcesses(), process =>
            {
#endif
                try
                {
                    Pnkv.TryAdd((uint)process.Id, process.ProcessName);
                }
                catch
                {
                    // ignored
                }
            });

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                string[] tokens = e.Data.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);

                string? protocolType = default;

                if (tokens.Length < 3 || !TypeLimited.Contains(protocolType = tokens.First()) || !uint.TryParse(tokens.Last(), out uint pid)) return;

#if NET
                string pName = Pnkv.GetValueOrDefault(pid, "[未知进程]");
#else
                string pName = Pnkv.TryGetValue(pid, out pName) ? pName : "未知进程";
#endif

                
                string localEndPoint = tokens[1];

#if NET
                IPEndPoint v = IPEndPoint.Parse(localEndPoint);
#else
                IPEndPoint v;
                {
                    int lt = localEndPoint.LastIndexOf(':');

                    if (!IPAddress.TryParse(localEndPoint.Substring(0, lt), out var pt1) || !int.TryParse(localEndPoint.Substring(lt + 1), out var pt2))
                    {
                        return;
                    }

                    v = new IPEndPoint(pt1, pt2);
                }
#endif
                switch (tokens.First())
                {
                    case "TCP" when tokens[3] != "LISTENING":
                    case "UDP" when tokens[2] != "*:*":
                        return;
                }

                connections.Add(new LocalConnection()
                {
                    Type = protocolType == "TCP" ? LocalConnectonType.TCP : LocalConnectonType.UDP,
                    EndPoint = v,
                    ProcessId = pid,
                    ProcessName = pName
                });
            };
            if (process.Start())
            {
                process.BeginOutputReadLine();

                try
                {
#if NET
                    await process.WaitForExitAsync(cancellationToken);
#else
                    await Task.Run(process.WaitForExit, cancellationToken).WhenAnyTime(cancellationToken);
#endif
                }
                catch (TaskCanceledException)
                {

                }
            }
            return connections;
        }
        public class LocalConnection
        {
            [JsonPropertyName("type")]
            public LocalConnectonType Type { get; set; } = LocalConnectonType.Unknown;

            [JsonIgnore]
            public IPEndPoint EndPoint { get; set; } = new IPEndPoint(IPAddress.None, 0);

            [JsonPropertyName("endPoint")]
            public string EndPointString => $"{EndPoint.Address}:{EndPoint.Port}";

            [JsonPropertyName("id")]
            public uint ProcessId { get; set; } = 0 ;

            [JsonPropertyName("name")]
            public string ProcessName { get; set; } = string.Empty;
        }

        public enum LocalConnectonType : byte
        {
            Unknown = 255,
            TCP = 0,
            UDP
        }
    }
}
