using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;


namespace OpenFrp.Service.Helpers
{
    public static partial class MessageBoxHelper
    {
        // Thanks SakuraFrp Launcher here::
        // https://github.com/natfrp/launcher-windows

#if NETFRAMEWORK
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
#elif NET
        [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);
#endif


        public enum MessageMode : uint
        {
            Ok = 0u,
            OkCancel = 1u,
            RetryCancel = 5u,
            AbortRetryIgnore = 2u,
            Error = 16u,
            Confirm = 32u,
            Warning = 48u,
            Info = 64u,
            // Modes 2
            SetForeground = 65535u
        }

        public enum MessageResult
        {
            Ok = 1,
            Cancel,
            Abort,
            Retry,
            Ignore
        }

    }
}
