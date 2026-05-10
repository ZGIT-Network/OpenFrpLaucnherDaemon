using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenFrp.Service;

namespace OpenFrp.Service.Helpers
{
    public static class ProcessHelper
    {
        public static Task<Process?> StartAsync(ProcessStartInfo psi)
        {
            return Task.Run<Process?>(() => Process.Start(psi));
        }

        public static Task<bool> StartAsync(this Process process)
        {
            return Task.Run(process.Start);
        }

        public static string GetMainModuleFileName(this Process process)
        {
            return process.MainModule?.FileName ?? string.Empty;
        }

        public static void OpenLink(string link)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = link,
                    UseShellExecute = true
                });
                return;
            }
            catch { }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "start",
                    Arguments = link,
                    UseShellExecute = true
                });
            }
            catch { }
        }


        public static async Task WaitForExitAsync(this Process process, int delay,CancellationToken cancellationToken = default)
        {
            await Task.Run(() => process.WaitForExit(delay)).WhenAnyTime(cancellationToken);
        }
        public static async Task WaitForExitAsync(this Process process,TimeSpan timeSpan, CancellationToken cancellationToken = default)
        {
#if NET
            await Task.Run(() => process.WaitForExit(timeSpan)).WhenAnyTime(cancellationToken);
#else
            await Task.Run(() => process.WaitForExit((int)timeSpan.TotalSeconds)).WhenAnyTime(cancellationToken);
#endif
        }
#if NETFRAMEWORK
        public static async Task WaitForExitAsync(this Process process,CancellationToken cancellationToken = default)
        {
            await Task.Run(process.WaitForExit).WhenAnyTime(cancellationToken);
        }
#endif

    }
}
