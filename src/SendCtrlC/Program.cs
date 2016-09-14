using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using NDesk.Options;

namespace SendCtrlC
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var findByName = true;
            int? timeout = null;
            var kill = false;
            var showHelp = false;

            var optionSet = new OptionSet
            {
                {"n|name", "find process by name (default)", x => { }},
                {"p|pid", "find process by pid", x => findByName = x != null},
                {"t|timeout=", "maximum amount of time to wait (in milliseconds) for program to exit", x => timeout = int.Parse(x)},
                {"k|kill", "kill after timeout", x => kill = x != null},
                {"?|help", "display this help and exit", x => showHelp = x != null}
            };

            try
            {
                var arguments = optionSet.Parse(args);

                if (showHelp)
                {
                    PrintHelp(optionSet);
                    return 0;
                }

                if (!arguments.Any())
                {
                    PrintHelp(optionSet, Console.Error);
                    return 1;
                }

                var processes = findByName
                    ? arguments.SelectMany(Process.GetProcessesByName).ToList()
                    : arguments.Select(int.Parse).Select(Process.GetProcessById).ToList();

                if (!processes.Any())
                {
                    PrintError("Couldn't find any processes by the supplied criteria");
                    return 1;
                }

                var hadErrors = false;

                foreach (var process in processes.Where(x => !x.HasExited))
                {
                    Console.WriteLine($"Shutting down {process.ProcessName} ({process.Id})...");

                    if (SetForegroundWindow(process.MainWindowHandle) || SetForegroundWindow(process.GetParentProcess().MainWindowHandle))
                    {
                        SendKeys.SendWait("^C");
                    }
                    else
                    {
                        PrintError($"Couldn\'t find window for {process.ProcessName} ({process.Id})");

                        if (kill)
                        {
                            process.Kill();
                            Console.WriteLine($"Killed {process.ProcessName} ({process.Id})");
                        }
                        else
                        {
                            hadErrors = true;
                        }
                    }
                }

                const int interval = 5000;
                Func<int?> getCurrentInterval = () => interval;

                if (timeout != null)
                {
                    var fullWaitIntervals = timeout.Value/interval;
                    var finalWaitInterval = timeout.Value%interval;

                    var count = 0;
                    getCurrentInterval = () =>
                    {
                        if (count < fullWaitIntervals)
                        {
                            ++count;
                            return interval;
                        }

                        if (count == fullWaitIntervals && finalWaitInterval > 0)
                        {
                            ++count;
                            return finalWaitInterval;
                        }

                        return null;
                    };
                }

                int? currentInterval = null;
                List<Process> processesStillRunning = null;

                Func<bool> continuePolling = () => (currentInterval = getCurrentInterval()) != null;
                Func<bool> hasProcessesStillRunning = () => (processesStillRunning = processes.Where(x => !x.HasExited).ToList()).Any();

                while (continuePolling() && hasProcessesStillRunning())
                {
                    var tasks = processesStillRunning
                        .Select(x => Task.Run(new Action(() => x.WaitForExit(currentInterval.Value))))
                        .ToArray();

                    Task.WaitAll(tasks);

                    Console.WriteLine("...");
                }

                if (hasProcessesStillRunning())
                {
                    processesStillRunning.ForEach(x =>
                    {
                        if (kill)
                        {
                            x.Kill();
                            Console.WriteLine($"Killed {x.ProcessName} ({x.Id})");
                        }
                        else
                        {
                            PrintError($"{x.ProcessName} ({x.Id}) did not shut down");
                            hadErrors = true;
                        }
                    });
                }

                return hadErrors ? 1 : 0;
            }
            catch (Exception ex)
            {
                PrintError("Error: " + ex);
                return 1;
            }
        }

        private static void PrintHelp(OptionSet optionSet, TextWriter writer = null)
        {
            if (writer == null)
                writer = Console.Out;

            var assemblyName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName);

            writer.WriteLine("Usage: " + assemblyName + " [OPTIONS] process1 [process2 ...]");

            writer.WriteLine();

            optionSet.WriteOptionDescriptions(writer);
        }

        private static void PrintError(string message)
        {
            var prevColor = Console.ForegroundColor;

            Console.ForegroundColor = ConsoleColor.DarkRed;

            Console.Error.WriteLine(message);

            Console.ForegroundColor = prevColor;
        }

        //NOTE: see http://dotbay.blogspot.com/2009/07/finding-parent-of-process-in-c.html
        private static Process GetParentProcess(this Process process)
        {
            var parentPid = 0;
            var processPid = process.Id;
            uint TH32CS_SNAPPROCESS = 2;

            // Take snapshot of processes
            var hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

            if (hSnapshot == IntPtr.Zero)
            {
                return null;
            }

            var procInfo = new PROCESSENTRY32
            {
                dwSize = (uint) Marshal.SizeOf(typeof(PROCESSENTRY32))
            };


            if (Process32First(hSnapshot, ref procInfo) == false)
            {
                return null;
            }

            do
            {
                if (processPid == procInfo.th32ProcessID)
                {
                    parentPid = (int) procInfo.th32ParentProcessID;
                }
            }
            while (parentPid == 0 && Process32Next(hSnapshot, ref procInfo));

            return parentPid > 0 ? Process.GetProcessById(parentPid) : null;
        }
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
        
        [DllImport("kernel32.dll")]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        
        [DllImport("kernel32.dll")]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
