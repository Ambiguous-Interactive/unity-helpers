[CmdletBinding()]
param([Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9+/]+=*$')][string]$EncodedWorker)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Private Unity worker desktops require Windows.' }
Add-Type -TypeDefinition @'
namespace UnityCi
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Text;

    public static class DesktopWorker
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size;
            public string Reserved, Desktop, Title;
            public int X, Y, Width, Height, XChars, YChars, Fill, Flags;
            public short Show, ReservedSize;
            public IntPtr ReservedBytes, Input, Output, Error;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInfo
        {
            public IntPtr Process, Thread;
            public int ProcessId, ThreadId;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string executable, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int kind);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint code);

        public static int Run(string executable, string encoded)
        {
            string name = "uh-mode-" + Guid.NewGuid().ToString("N");
            IntPtr desktop = CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero, 0, 0x01ff, IntPtr.Zero);
            if (desktop == IntPtr.Zero) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create the Unity worker desktop."); }
            try
            {
                StartupInfo startup = new StartupInfo();
                startup.Size = Marshal.SizeOf<StartupInfo>();
                startup.Desktop = name;
                startup.Flags = 0x0100;
                startup.Input = GetStdHandle(-10);
                startup.Output = GetStdHandle(-11);
                startup.Error = GetStdHandle(-12);
                StringBuilder command = new StringBuilder("\"" + executable + "\" -NoProfile -NonInteractive -OutputFormat Text -EncodedCommand " + encoded);
                ProcessInfo process;
                if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08000000, IntPtr.Zero, null, ref startup, out process))
                { throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot launch the Unity worker on its private desktop."); }
                CloseHandle(process.Thread);
                try
                {
                    if (WaitForSingleObject(process.Process, 0xffffffff) != 0) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot wait for the Unity worker."); }
                    uint code;
                    if (!GetExitCodeProcess(process.Process, out code)) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read the Unity worker exit code."); }
                    return unchecked((int)code);
                }
                finally { CloseHandle(process.Process); }
            }
            finally { CloseDesktop(desktop); }
        }
    }
}
'@
exit [UnityCi.DesktopWorker]::Run([Environment]::ProcessPath, $EncodedWorker)
