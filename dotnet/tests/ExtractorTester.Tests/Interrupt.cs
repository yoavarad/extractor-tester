using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ExtractorTester.Tests;

/// <summary>Start the exe, wait until it compared an item, then interrupt it like Ctrl-C.</summary>
/// <remarks>
/// As tests/test_runner_progress.py: POSIX sends SIGINT; Windows starts the exe in a new process
/// group (Process.Start cannot) so CTRL_BREAK reaches only that group.
/// </remarks>
internal static partial class Interrupt
{
    private static readonly Regex Compared = new(@"compared [1-9]\d*/");
    private const int StartWaitMs = 60_000;

    /// <summary>Run, interrupt with SIGINT, return (exit code, stderr).</summary>
    public static (int Code, string Stderr) RunPosix(ProcessStartInfo psi)
    {
        using var proc = Process.Start(psi)!;
        var seen = new StringBuilder();
        var reader = Task.Run(() =>
        {
            var buf = new char[256];
            int n;
            while ((n = proc.StandardError.Read(buf, 0, buf.Length)) > 0)
            {
                lock (seen)
                {
                    seen.Append(buf, 0, n);
                }
            }
        });
        _ = proc.StandardOutput.ReadToEndAsync();
        WaitFor(() =>
        {
            lock (seen)
            {
                return seen.ToString();
            }
        });
        Assert.False(proc.HasExited, "run finished before interrupt");
        using (var kill = Process.Start("kill", ["-INT", proc.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
        {
            kill.WaitForExit();
            Assert.Equal(0, kill.ExitCode);
        }
        Assert.True(proc.WaitForExit(60_000), "no exit after interrupt");
        reader.Wait(60_000);
        lock (seen)
        {
            return (proc.ExitCode, seen.ToString());
        }
    }

    /// <summary>Run in a new process group with stderr in a file, send CTRL_BREAK, return (exit code, stderr).</summary>
    public static (int Code, string Stderr) RunWindows(string exe, string[] args, string stderrPath)
    {
        var sa = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        var file = CreateFileW(stderrPath, GenericWrite, FileShareRead | FileShareWrite, ref sa, CreateAlways, 0, IntPtr.Zero);
        if (file == new IntPtr(-1))
        {
            throw new Win32Exception();
        }
        var cmd = new StringBuilder(Quote(exe));
        foreach (var a in args)
        {
            cmd.Append(' ').Append(Quote(a));
        }
        var si = new StartupInfo
        {
            Cb = Marshal.SizeOf<StartupInfo>(),
            Flags = StartfUseStdHandles,
            StdInput = IntPtr.Zero,
            StdOutput = file,
            StdError = file,
        };
        if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, true, CreateNewProcessGroup, IntPtr.Zero, null, ref si, out var pi))
        {
            var e = new Win32Exception();
            CloseHandle(file);
            throw e;
        }
        CloseHandle(file);
        CloseHandle(pi.Thread);
        try
        {
            WaitFor(() => ReadShared(stderrPath));
            Assert.Equal(WaitTimeout, WaitForSingleObject(pi.Process, 0)); // still running
            Assert.True(GenerateConsoleCtrlEvent(CtrlBreakEvent, pi.ProcessId), new Win32Exception().Message);
            Assert.Equal(0u, WaitForSingleObject(pi.Process, 60_000));
            Assert.True(GetExitCodeProcess(pi.Process, out var code));
            return ((int)code, ReadShared(stderrPath));
        }
        finally
        {
            CloseHandle(pi.Process);
        }
    }

    private static void WaitFor(Func<string> output)
    {
        var deadline = Stopwatch.StartNew();
        while (!Compared.IsMatch(output()))
        {
            Assert.True(deadline.ElapsedMilliseconds < StartWaitMs, "no item compared in time: " + output());
            Thread.Sleep(20);
        }
    }

    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static string Quote(string a) => "\"" + a.Replace("\"", "\\\"") + "\"";

    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint CreateAlways = 2;
    private const int StartfUseStdHandles = 0x100;
    private const uint CreateNewProcessGroup = 0x200;
    private const uint CtrlBreakEvent = 1;
    private const uint WaitTimeout = 0x102;

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2a;
        public IntPtr Reserved2b;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string name, uint access, uint share, ref SecurityAttributes sa, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
