using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.ComponentModel; // Added for Win32Exception

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
            Console.WriteLine("6. Graceful System Restart");
            Console.WriteLine("7. Graceful System Shutdown");
            Console.WriteLine("8. KILL ALL USER PROCESSES & RESTART SYSTEM (EXTREME CAUTION!)");
            Console.WriteLine("9. KILL ALL USER PROCESSES & SHUTDOWN SYSTEM (EXTREME CAUTION!)");
            Console.WriteLine("10. Kill All Processes (Advanced)"); // NEW POSITION
            Console.WriteLine("11. Exit"); // NEW POSITION
            Console.Write("Enter your choice: ");

            string choice = (Console.ReadLine() ?? string.Empty).Trim(); // Improved: handle null and whitespace
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
                    KillProcessesByName(pName);
                    break;
                case "6":
                    GracefulSystemRestart();
                    return; // Application exits after initiating restart
                case "7":
                    GracefulSystemShutdown();
                    return; // Application exits after initiating shutdown
                case "8":
                    KillAllUserProcessesAndRestart();
                    return; // Application exits after initiating restart
                case "9":
                    KillAllUserProcessesAndShutdown();
                    return; // Application exits after initiating shutdown
                case "10": // NEW CASE POSITION
                    KillAllProcessesSubMenu();
                    break;
                case "11": // NEW CASE POSITION
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
    /// Displays a sub-menu for advanced process killing options.
    /// </summary>
    public static void KillAllProcessesSubMenu()
    {
        while (true)
        {
            Console.WriteLine("\n--- Kill All Processes (Advanced) ---");
            Console.WriteLine("1. Kill All Foreground Processes (has GUI)");
            Console.WriteLine("2. Kill All Background Processes (no GUI)");
            Console.WriteLine("3. Kill ALL User Processes (Foreground & Background)");
            Console.WriteLine("4. Back to Main Menu");
            Console.Write("Enter your choice: ");

            string subChoice = (Console.ReadLine() ?? string.Empty).Trim();
            Console.WriteLine();

            switch (subChoice)
            {
                case "1":
                    KillProcessesByWindowStatus(isForeground: true);
                    break;
                case "2":
                    KillProcessesByWindowStatus(isForeground: false);
                    break;
                case "3":
                    // This method already handles "all" user processes
                    KillAllNonCriticalUserProcesses();
                    break;
                case "4":
                    return; // Exit sub-menu
                default:
                    Console.WriteLine("Invalid choice. Please try again.");
                    break;
            }
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
            Console.Clear(); // Clear sub-menu after operation, before re-displaying main menu
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

    /// <summary>
    /// Attempts to kill all non-critical processes belonging to the current user's session,
    /// filtered by whether they have a main window (foreground) or not (background).
    /// </summary>
    /// <param name="isForeground">True to target foreground processes, false to target background processes. If null, targets all user processes.</param>
    /// <returns>True if the user confirmed the action and killing started, false otherwise.</returns>
    private static bool KillProcessesByWindowStatus(bool? isForeground)
    {
        string targetType = isForeground switch
        {
            true => "Foreground",
            false => "Background",
            null => "ALL",
        };

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n!!! CAUTION: You are about to kill ALL {targetType} processes associated with your user session. !!!");
        Console.WriteLine("This will likely close ALL open applications of this type and may lead to data loss.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"Are you absolutely sure you want to proceed with killing ALL {targetType} user processes? (type 'YES' to confirm): ");
        Console.ResetColor();

        if ((Console.ReadLine() ?? string.Empty).ToUpper() != "YES")
        {
            Console.WriteLine($"Kill all {targetType} processes operation cancelled by user.");
            return false;
        }

        Console.WriteLine($"\nAttempting to kill all non-critical {targetType} user processes...");

        int currentSessionId = Process.GetCurrentProcess().SessionId;
        int currentProcessId = Process.GetCurrentProcess().Id;

        // Blacklist of processes that are typically essential system components or
        // processes that are too critical to be killed even within a user session
        string[] criticalSystemProcessesToSkip = new string[]
        {
            "smss",     // Session Manager Subsystem (CRITICAL)
            "csrss",    // Client/Server Runtime Subsystem (CRITICAL)
            "wininit",  // Windows Start-up Application (CRITICAL)
            "services", // Services Control Manager (CRITICAL)
            "lsass",    // Local Security Authority Process (CRITICAL)
        };

        var processesToTarget = Process.GetProcesses()
                                        .Where(p => p.SessionId == currentSessionId && p.Id != currentProcessId)
                                        .Where(p => !criticalSystemProcessesToSkip.Any(critName => p.ProcessName.Equals(critName, StringComparison.OrdinalIgnoreCase)))
                                        .Where(p => isForeground == null || (isForeground.Value == !string.IsNullOrEmpty(p.MainWindowTitle)))
                                        .ToList(); // To avoid issues if process list changes during iteration

        if (!processesToTarget.Any())
        {
            Console.WriteLine($"No non-critical {targetType} processes found in your session to kill.");
            return false;
        }

        foreach (var p in processesToTarget)
        {
            try
            {
                Console.WriteLine($"  -> Attempting to kill process: {p.ProcessName} (ID: {p.Id})");
                p.Kill();
                p.WaitForExit(2000); // Wait up to 2 seconds for each process to exit
                if (p.HasExited)
                {
                    Console.WriteLine($"     Process {p.Id} killed.");
                }
                else
                {
                    Console.WriteLine($"     Process {p.Id} did not exit after 2 seconds.");
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5) // Access Denied
            {
                Console.WriteLine($"     Access Denied: Cannot kill process {p.ProcessName} (ID: {p.Id}). Requires higher privilege or is protected.");
            }
            catch (InvalidOperationException)
            {
                // Process might have already exited or is no longer accessible
                Console.WriteLine($"     Process {p.ProcessName} (ID: {p.Id}) already exited or cannot be accessed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"     Error killing process {p.ProcessName} (ID: {p.Id}): {ex.Message}");
            }
        }
        Console.WriteLine($"\nFinished attempting to kill non-critical {targetType} user processes.");
        return true;
    }

    /// <summary>
    /// Attempts to kill all non-critical processes belonging to the current user's session.
    /// This is an EXTREMELY DANGEROUS operation and can lead to data loss or system instability.
    /// This method is now a wrapper around the more generic KillProcessesByWindowStatus for consistency.
    /// </summary>
    /// <returns>True if the user confirmed the action and killing started, false otherwise.</returns>
    private static bool KillAllNonCriticalUserProcesses()
    {
        // Calling the generic method with null for isForeground to target all
        return KillProcessesByWindowStatus(isForeground: null);
    }

    /// <summary>
    /// Kills all non-critical user processes and then initiates a system restart.
    /// Requires Administrator privileges.
    /// </summary>
    public static void KillAllUserProcessesAndRestart()
    {
        if (KillAllNonCriticalUserProcesses())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nInitiating system restart (this window will close)...");
            Console.ResetColor();
            try
            {
                var psi = new ProcessStartInfo("shutdown.exe", "/r /t 0")
                {
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                Thread.Sleep(2000); // Give shutdown command more time to register
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                Console.WriteLine("Error: Access Denied. Cannot initiate system restart. Run the application as Administrator.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initiating system restart: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Kills all non-critical user processes and then initiates a system shutdown.
    /// Requires Administrator privileges.
    /// </summary>
    public static void KillAllUserProcessesAndShutdown()
    {
        if (KillAllNonCriticalUserProcesses())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nInitiating system shutdown (this window will close)...");
            Console.ResetColor();
            try
            {
                var psi = new ProcessStartInfo("shutdown.exe", "/s /t 0")
                {
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                Thread.Sleep(2000); // Give shutdown command more time to register
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                Console.WriteLine("Error: Access Denied. Cannot initiate system shutdown. Run the application as Administrator.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initiating system shutdown: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Initiates a standard, graceful system restart.
    /// Requires Administrator privileges.
    /// </summary>
    public static void GracefulSystemRestart()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nInitiating a graceful system restart (this window will close)...");
        Console.WriteLine("All applications will be given a chance to save their work and close.");
        Console.ResetColor();
        try
        {
            var psi = new ProcessStartInfo("shutdown.exe", "/r /t 0")
            {
                UseShellExecute = true,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Thread.Sleep(2000); // Give shutdown command more time to register
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            Console.WriteLine("Error: Access Denied. Cannot initiate system restart. Run the application as Administrator.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initiating system restart: {ex.Message}");
        }
    }

    /// <summary>
    /// Initiates a standard, graceful system shutdown.
    /// Requires Administrator privileges.
    /// </summary>
    public static void GracefulSystemShutdown()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nInitiating a graceful system shutdown (this window will close)...");
        Console.WriteLine("All applications will be given a chance to save their work and close.");
        Console.ResetColor();
        try
        {
            var psi = new ProcessStartInfo("shutdown.exe", "/s /t 0")
            {
                UseShellExecute = true,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Thread.Sleep(2000); // Give shutdown command more time to register
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            Console.WriteLine("Error: Access Denied. Cannot initiate system shutdown. Run the application as Administrator.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initiating system shutdown: {ex.Message}");
        }
    }
}
