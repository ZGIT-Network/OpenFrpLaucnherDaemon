using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace OpenFrp.Service
{
    internal partial class Win32Native
    {
        public enum ServiceType : uint
        {
            SERVICE_WIN32_OWN_PROCESS = 0x00000010,
            SERVICE_WIN32_SHARE_PROCESS = 0x00000020,
            SERVICE_INTERACTIVE_PROCESS = 0x00000100,
            SERVICE_NO_CHANGE = 0xFFFFFFFF
        }

        public enum ServiecConfigType : uint
        {
            SERVICE_CONFIG_DESCRIPTION = 0x00000001,
            SERVICE_CONFIG_FAILURE_ACTIONS = 0x00000002,
            SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 0x00000003,
            SERVICE_CONFIG_FAILURE_ACTIONS_FLAG = 0x00000004,
            SERVICE_CONFIG_SERVICE_SID_INFO = 0x00000005,
            SERVICE_CONFIG_REQUIRED_PRIVILEGES_INFO = 0x00000006,
            SERVICE_CONFIG_PRESHUTDOWN_INFO = 0x00000007,
            SERVICE_CONFIG_TRIGGER_INFO = 0x00000008,
            SERVICE_CONFIG_PREFERRED_NODE = 0x00000009
        }

        public enum ServiceManagerAccess : uint
        {
            SC_MANAGER_CONNECT = 0x0001,
            SC_MANAGER_CREATE_SERVICE = 0x0002,
            SC_MANAGER_ENUMERATE_SERVICE = 0x0004,
            SC_MANAGER_LOCK = 0x0008,
            SC_MANAGER_QUERY_LOCK_STATUS = 0x0010,
            SC_MANAGER_MODIFY_BOOT_CONFIG = 0x0020,
            SC_MANAGER_ALL_ACCESS = 0xF003F
        }

        public enum ServiceStartType : uint
        {
            SERVICE_AUTO_START = 0x00000002,
            SERVICE_BOOT_START = 0x00000000,
            SERVICE_DEMAND_START = 0x00000003,
            SERVICE_DISABLED = 0x00000004,
            SERVICE_SYSTEM_START = 0x00000001,
        }

        public enum ServiceErrorControl : uint
        {
            SERVICE_ERROR_IGNORE = 0x00000000,
            SERVICE_ERROR_NORMAL = 0x00000001,
            SERVICE_ERROR_SEVERE = 0x00000002,
            SERVICE_ERROR_CRITICAL = 0x00000003
        }

        public enum ControlServiceResult : uint
        {
            ERROR_SERVICE_EXISTS = 0x000000B7, // The specified service already exists as a service installed in the system.
            ERROR_SERVICE_MARKED_FOR_DELETE = 0x0000006B, // The specified service has been marked for deletion.
            ERROR_INVAILD_PARAMETER = 0x00000057, // The parameter is incorrect.
            ERROR_INVAILD_HANDLE = 0x00000006, // The handle is invalid.
            ERROR_INSUFFICIENT_BUFFER = 0x0000007A, // The data area passed to a system call is too small.
        }

        /// <summary>
        /// ACL 权限枚举
        /// </summary>
        public enum ServiceAccessRights : uint
        {
            SERVICE_QUERY_CONFIG = 0x0001,
            SERVICE_CHANGE_CONFIG = 0x0002,
            SERVICE_QUERY_STATUS = 0x0004,
            SERVICE_ENUMERATE_DEPENDENTS = 0x0008,
            SERVICE_START = 0x0010,
            SERVICE_STOP = 0x0020,
            SERVICE_PAUSE_CONTINUE = 0x0040,
            SERVICE_INTERROGATE = 0x0080,
            SERVICE_USER_DEFINED_CONTROL = 0x0100,
            SERVICE_ALL_ACCESS = 0xF01FF
        }

        public enum ServiceState : uint
        {
            SERVICE_STOPPED = 0x00000001,
            SERVICE_START_PENDING = 0x00000002,
            SERVICE_STOP_PENDING = 0x00000003,
            SERVICE_RUNNING = 0x00000004,
            SERVICE_CONTINUE_PENDING = 0x00000005,
            SERVICE_PAUSE_PENDING = 0x00000006,
            SERVICE_PAUSED = 0x00000007
        }

        public enum FormatMessageFlags : uint
        {
            FORMAT_MESSAGE_ALLOCATE_BUFFER = 0x00000100,
            FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200,
            FORMAT_MESSAGE_FROM_STRING = 0x00000400,
            FORMAT_MESSAGE_FROM_HMODULE = 0x00000800,
            FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000,
            FORMAT_MESSAGE_ARGUMENT_ARRAY = 0x00002000,
            FORMAT_MESSAGE_MAX_WIDTH_MASK = 0x000000FF
        }

        public enum ServiceControlType : uint
        {             
            SERVICE_CONTROL_STOP = 0x00000001,
            SERVICE_CONTROL_PAUSE = 0x00000002,
            SERVICE_CONTROL_CONTINUE = 0x00000003,
            SERVICE_CONTROL_INTERROGATE = 0x00000004,
            SERVICE_CONTROL_SHUTDOWN = 0x00000005,
            SERVICE_CONTROL_PARAMCHANGE = 0x00000006,
            SERVICE_CONTROL_NETBINDADD = 0x00000007,
            SERVICE_CONTROL_NETBINDREMOVE = 0x00000008,
            SERVICE_CONTROL_NETBINDENABLE = 0x00000009,
            SERVICE_CONTROL_NETBINDDISABLE = 0x0000000A
        }

        public enum ServiceControlStopReason : uint
        {
            SERVICE_STOP_REASON_FLAG_PLANNED = 0x40000000,
            SERVICE_STOP_REASON_FLAG_UNPLANNED = 0x10000000,
            SERVICE_STOP_REASON_FLAG_CUSTOM = 0x20000000,
            SERVICE_STOP_REASON_MAJOR_APPLICATION = 0x00050000,
        }

        public enum ServiceAcceptType : uint
        {
            SERVICE_ACCEPT_STOP = 0x00000001,
            SERVICE_ACCEPT_SHUTDOWN = 0x00000004,
        }


        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_DESCRIPTION
        {
            public string lpDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS_PROCESS
        {
            public ServiceType dwServiceType;

            public ServiceState dwCurrentState;

            public ServiceAcceptType dwControlsAccepted;

            /** The following fields are not used in this program, removed. */


            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
            public uint dwProcessId;
            public uint dwServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_CONTROL_STATUS_REASON_PARAMSA
        {
            public string pszComment;

            public ServiceControlStopReason dwReason;

            public SERVICE_STATUS_PROCESS ServiceStatus;

        }

        [DllImport("advapi32.dll")]
        public static extern bool ChangeServiceConfig2A(
            IntPtr hService, uint dwInfoLevel,
            [MarshalAs(UnmanagedType.Struct)] ref SERVICE_DESCRIPTION lpInfo);

#if NET
        [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceObjectSecurity", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool QueryServiceObjectSecurity(
                nint serviceHandle,
                System.Security.AccessControl.SecurityInfos secInfo,
                byte[] lpSecDesrBuf,
                uint bufSize,
                out uint bufSizeNeeded);

        [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
        internal static partial nint OpenSCManagerA(
                string lpMachineName,
                string lpDatabaseName,
                uint dwDesiredAccess
            );

        [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
        internal static partial nint OpenServiceA(
                nint hSCManager,
                string lpServiceName,
                uint dwDesiredAccess
            );

        [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
        internal static partial nint CreateServiceA(
            nint hSCManager,
            string lpServiceName,
            string lpDisplayName,
            uint dwDesiredAccess,
            uint dwServiceType,
            uint dwStartType,
            uint dwErrorControl,
            string lpBinaryPathName,
            string lpLoadOrderGroup,
            nint lpdwTagId,
            string lpDependencies,
            string lpServiceStartName,
            string lpPassword
        );

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetServiceObjectSecurity(
            nint ServiceHandle, 
            SecurityInfos SecurityInformation, 
            byte[] SecurityDescriptor);

        [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true,StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool StartService(
            nint hService,
            uint dwNumServiceArgs,
            string[] lpServiceArgVectors);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool QueryServiceStatusEx(
             nint hService,
             uint infoLevel,
             ref SERVICE_STATUS_PROCESS lpBuffer,
             uint bufSize,
             out uint bytesNeeded);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseServiceHandle(nint hSCObject);

        [LibraryImport("Kernel32.dll", SetLastError = true)]
        internal static partial uint FormatMessage(uint dwFlags,
            nint lpSource,
            uint dwMessageId, 
            uint dwLanguageId, 
            ref nint lpBuffer,
            uint nSize, nint pArguments);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ControlServiceExA(
                nint hService,
                ServiceControlType dwControl,
                uint dwInfoLevel,
                nint pControlParams);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ControlService(
            IntPtr hService,
            ServiceControlType dwControl,
            ref SERVICE_STATUS_PROCESS lpServiceStatus
        );

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteService(nint hService);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial IntPtr LocalFree(IntPtr hMem);
#else
        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool QueryServiceObjectSecurity(
                IntPtr serviceHandle,
                System.Security.AccessControl.SecurityInfos secInfo,
                byte[] lpSecDesrBuf,
                uint bufSize,
                out uint bufSizeNeeded);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern IntPtr OpenSCManagerA(
                string lpMachineName,
                string lpDatabaseName,
                uint dwDesiredAccess
            );

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern IntPtr OpenServiceA(
                IntPtr hSCManager,
                string lpServiceName,
                uint dwDesiredAccess
            );

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern IntPtr CreateServiceA(
            IntPtr hSCManager,
            string lpServiceName,
            string lpDisplayName,
            uint dwDesiredAccess,
            uint dwServiceType,
            uint dwStartType,
            uint dwErrorControl,
            string lpBinaryPathName,
            string lpLoadOrderGroup,
            IntPtr lpdwTagId,
            string lpDependencies,
            string lpServiceStartName,
            string lpPassword
        );

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool SetServiceObjectSecurity(
            IntPtr ServiceHandle, 
            SecurityInfos SecurityInformation, 
            byte[] SecurityDescriptor);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool StartService(
            IntPtr hService,
            uint dwNumServiceArgs,
            string[] lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool QueryServiceStatusEx(
            IntPtr hService,
            uint infoLevel,
            ref SERVICE_STATUS_PROCESS lpBuffer,
            uint bufSize,
            out uint bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool CloseServiceHandle(IntPtr hSCObject);

        [DllImport("Kernel32.dll", SetLastError = true)]
        internal static extern uint FormatMessage(uint dwFlags, 
            IntPtr lpSource,
            uint dwMessageId, 
            uint dwLanguageId, 
            ref IntPtr lpBuffer,
            uint nSize, IntPtr pArguments);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr LocalFree(IntPtr hMem);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool ControlServiceExA(
            IntPtr hService,
            ServiceControlType dwControl,
            uint dwInfoLevel,
            IntPtr pControlParams
        );

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool ControlService(
            IntPtr hService,
            ServiceControlType dwControl,
            ref SERVICE_STATUS_PROCESS lpServiceStatus
        );

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool DeleteService(IntPtr hService);
#endif

        public static string GetFormatMessage(uint code)
        {
            IntPtr lpMsgBuf = IntPtr.Zero;

            // 函数忽略消息定义文本中的常规换行符。 函数将消息定义文本中的硬编码换行符存储到输出缓冲区中。
            // 函数不生成新的换行符。(FORMAT_MESSAGE_MAX_WIDTH_MASK = 0x000000FF)

            uint dwChars = Win32Native.FormatMessage(0x000000FF | (uint)(Win32Native.FormatMessageFlags.FORMAT_MESSAGE_ALLOCATE_BUFFER | Win32Native.FormatMessageFlags.FORMAT_MESSAGE_FROM_SYSTEM | Win32Native.FormatMessageFlags.FORMAT_MESSAGE_IGNORE_INSERTS)
                , IntPtr.Zero, code, 0, ref lpMsgBuf, 0, IntPtr.Zero);

            if (dwChars == 0) { return string.Empty; }

            string? sRet = Marshal.PtrToStringAnsi(lpMsgBuf);

            lpMsgBuf = LocalFree(lpMsgBuf);

            return sRet?.Remove(sRet.Length - 1) ?? string.Empty;
        }

    }
}
