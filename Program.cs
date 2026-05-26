using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

public class ProcessManager
{
    private static Dictionary<int, TimeSpan> _previousCpuTimes = new Dictionary<int, TimeSpan>();
    private static DateTime _lastSampleTime = DateTime.Now;
    private static readonly object _lock = new object(); // To synchronize access to _previousCpuTimes and _lastSampleTime

    public static void Main(string[] args)
    {
        Console.WriteLine("Welcome to the Custom Process Manager!");
        Console.WriteLine("-------------------------------------");

        // IMPORTANT: For many features (especially killing processes),
        // this application needs to be run as an Administrator.
        // Right-click the executable -> Run as administrator.

        while (true)
        {
            Console.WriteLine("\nChoose an option:");
            Console.WriteLine("1. List all processes");
            Console.WriteLine("2. List processes by CPU Usage (High to Low)");
            Console.WriteLine("3. List background processes");
            Console.WriteLine("4. Kill a process by ID");
            Console.WriteLine("5. Kill processes by Name");
            Console.WriteLine("6. Exit");
            Console.Write("Enter your choice: ");

            string choice = Console.ReadLine();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    ListAllProcesses();
                    break;
                case "2":
                    ListProcessesByCpuUsage();
                    break;
                case "3":
                    ListBackgroundProcesses();
                    break;
                case "4":
                    Console.Write("Enter the Process ID to kill: ");
                    if (int.TryParse(Console.ReadLine() ?? string.Empty, out int pid))
                    {
                        KillProcessById(pid);
                    }
                    else
                    {
                        Console.WriteLine("Invalid Process ID.");
                    }
                    break;
                case "5":
                    Console.Write("Enter the Process Name to kill: ");
                    string pName = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(pName))
                    {
                        KillProcessesByName(pName);
                    }
                    else
                    {
                        Console.WriteLine("Process name cannot be empty.");
                    }
                    break;
                case "6":
                    Console.WriteLine("Exiting Process Manager. Goodbye!");
                    return;
                default:
                    Console.WriteLine("Invalid choice. Please try again.");
                    break;
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
            Console.Clear();
        }
    }

    /// <summary>
    /// Lists all running processes with their ID, Name, and Memory Usage.
    /// </summary>
    public static void ListAllProcesses()
    {
        Console.WriteLine("Listing all running processes:");
        Console.WriteLine("{0,-7} {1,-40} {2,-15} {3,-10}", "ID", "Name", "Memory (MB)", "Has GUI");
        Console.WriteLine("------------------------------------------------------------------------------------");

        var processes = Process.GetProcesses().OrderBy(p => p.ProcessName);

        foreach (var p in processes)
        {
            try
            {
                Console.WriteLine("{0,-7} {1,-40} {2,-15:N0} {3,-10}",
                    p.Id,
                    TruncateString(p.ProcessName, 38), // Truncate long names
                    p.WorkingSet64 / (1024 * 1024), // Convert bytes to MB
                    !string.IsNullOrEmpty(p.MainWindowTitle) // Check if it has a main window
                );
            }
            catch (Exception)
            {
                // Accessing properties of some system processes can throw exceptions (e.g., Access Denied).
                // We'll just display what we can or indicate "N/A".
                Console.WriteLine("{0,-7} {1,-40} {2,-15} {3,-10}", p.Id, TruncateString(p.ProcessName, 38), "N/A", "N/A");
            }
        }
    }

    /// <summary>
    /// Lists processes, sorted by estimated CPU usage (high to low).
    /// </summary>
    public static void ListProcessesByCpuUsage()
    {
        Console.WriteLine("Estimating CPU usage over a short interval (this may take a moment)...");

        // First snapshot
        var firstSnapshot = GetCpuTimesSnapshot();
        Thread.Sleep(500); // Wait for 0.5 seconds for a more accurate delta

        // Second snapshot
        var secondSnapshot = GetCpuTimesSnapshot();
        double timeElapsedSeconds = 0.5; // Our wait time
        int numberOfCores = Environment.ProcessorCount;

        Console.WriteLine("\nProcesses sorted by estimated CPU Usage:");
        Console.WriteLine("{0,-7} {1,-40} {2,-15} {3,-10} {4,-10}", "ID", "Name", "Memory (MB)", "CPU (%)", "Has GUI");
        Console.WriteLine("--------------------------------------------------------------------------------------------");

        var processCpuInfo = new List<(Process p, double cpu, long memory, bool hasGui)>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                double cpuPercent = 0;
                if (firstSnapshot.ContainsKey(p.Id) && secondSnapshot.ContainsKey(p.Id))
                {
                    TimeSpan cpuTimeElapsed = secondSnapshot[p.Id] - firstSnapshot[p.Id];
                    // CPU percentage = (CPU time elapsed for process / (time elapsed for sampling * number of cores)) * 100
                    cpuPercent = (cpuTimeElapsed.TotalMilliseconds / (timeElapsedSeconds * 1000.0 * numberOfCores)) * 100.0;
                    if (cpuPercent < 0) cpuPercent = 0; // Guard against negative values (e.g., if process started/stopped)
                }
                
                processCpuInfo.Add((p, cpuPercent, p.WorkingSet64 / (1024 * 1024), !string.IsNullOrEmpty(p.MainWindowTitle)));
            }
            catch (Exception)
            {
                // Access denied or process exited.
                processCpuInfo.Add((p, 0, 0, false)); // Default to 0 CPU, 0 memory, no GUI
            }
        }

        foreach (var info in processCpuInfo.OrderByDescending(i => i.cpu))
        {
            if (info.cpu > 0.01) // Only show processes with some measurable CPU activity
            {
                Console.WriteLine("{0,-7} {1,-40} {2,-15:N0} {3,-10:N1} {4,-10}",
                    info.p.Id,
                    TruncateString(info.p.ProcessName, 38),
                    info.memory,
                    info.cpu,
                    info.hasGui
                );
            }
        }
    }

    /// <summary>
    /// Takes a snapshot of TotalProcessorTime for all running processes.
    /// </summary>
    private static Dictionary<int, TimeSpan> GetCpuTimesSnapshot()
    {
        lock (_lock)
        {
            var snapshot = new Dictionary<int, TimeSpan>();
            foreach (var p in Process.GetProcesses())
            {
                try { snapshot[p.Id] = p.TotalProcessorTime; }
                catch (Exception) { /* Ignore processes that throw access denied */ }
            }
            return snapshot;
        }
    }

    /// <summary>
    /// Lists processes identified as "background" (no main window).
    /// </summary>
    public static void ListBackgroundProcesses()
    {
        Console.WriteLine("Listing background processes (heuristic: no main window):");
        Console.WriteLine("{0,-7} {1,-40} {2,-15}", "ID", "Name", "Memory (MB)");
        Console.WriteLine("--------------------------------------------------------------------");

        var backgroundProcesses = Process.GetProcesses()
                                        .Where(p => string.IsNullOrEmpty(p.MainWindowTitle))
                                        .OrderBy(p => p.ProcessName);

        foreach (var p in backgroundProcesses)
        {
            try
            {
                Console.WriteLine("{0,-7} {1,-40} {2,-15:N0}",
                    p.Id,
                    TruncateString(p.ProcessName, 38),
                    p.WorkingSet64 / (1024 * 1024)
                );
            }
            catch (Exception)
            {
                Console.WriteLine("{0,-7} {1,-40} {2,-15}", p.Id, TruncateString(p.ProcessName, 38), "N/A");
            }
        }
    }

    /// <summary>
    /// Attempts to kill a process by its Process ID.
    /// </summary>
    /// <param name="processId">The ID of the process to kill.</param>
    public static void KillProcessById(int processId)
    {
        try
        {
            Process processToKill = Process.GetProcessById(processId);

            // Optional: Ask for confirmation for critical processes
            if (processToKill.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                processToKill.ProcessName.Equals("csrss", StringComparison.OrdinalIgnoreCase) ||
                processToKill.ProcessName.Equals("smss", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"WARNING: Killing {processToKill.ProcessName} (ID: {processId}) can cause system instability or a crash.");
                Console.Write("Are you absolutely sure you want to proceed? (yes/no): ");
                if ((Console.ReadLine() ?? string.Empty).ToLower() != "yes")
                {
                    Console.WriteLine("Kill operation cancelled.");
                    return;
                }
            }

            Console.WriteLine($"Attempting to kill process: {processToKill.ProcessName} (ID: {processToKill.Id})");
            processToKill.Kill(); // Forcefully terminates the process
            processToKill.WaitForExit(5000); // Wait up to 5 seconds for the process to exit
            if (processToKill.HasExited)
            {
                Console.WriteLine($"Process {processId} killed successfully.");
            }
            else
            {
                Console.WriteLine($"Process {processId} did not exit after 5 seconds.");
            }
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"Process with ID {processId} not found.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5) // Access Denied
        {
            Console.WriteLine($"Access Denied: Cannot kill process {processId}. Try running the application as Administrator.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error killing process {processId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to kill all processes with a given name.
    /// </summary>
    /// <param name="processName">The name of the processes to kill.</param>
    public static void KillProcessesByName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            Console.WriteLine("Process name cannot be empty.");
            return;
        }

        var processesToKill = Process.GetProcessesByName(processName);
        if (processesToKill.Length == 0)
        {
            Console.WriteLine($"No processes found with name: '{processName}'");
            return;
        }

        // Optional: Ask for confirmation for critical processes by name
        if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("csrss", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"WARNING: Killing processes named '{processName}' can cause system instability or a crash.");
            Console.Write("Are you absolutely sure you want to proceed? (yes/no): ");
            if ((Console.ReadLine() ?? string.Empty).ToLower() != "yes")
            {
                Console.WriteLine("Kill operation cancelled.");
                return;
            }
        }

        Console.WriteLine($"Attempting to kill {processesToKill.Length} processes named: '{processName}'");
        foreach (var p in processesToKill)
        {
            try
            {
                Console.WriteLine($"  -> Killing process: {p.ProcessName} (ID: {p.Id})");
                p.Kill();
                p.WaitForExit(5000);
                if (p.HasExited)
                {
                    Console.WriteLine($"     Process {p.Id} killed.");
                }
                else
                {
                    Console.WriteLine($"     Process {p.Id} did not exit after 5 seconds.");
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                Console.WriteLine($"     Access Denied: Cannot kill process {p.Id}. Try running the application as Administrator.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"     Error killing process {p.Id}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Helper method to truncate long strings for console display.
    /// </summary>
    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
    }
}