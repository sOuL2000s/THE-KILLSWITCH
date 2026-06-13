using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ComponentModel;

namespace OneClickTaskKiller
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("One-Click Task Killer - Cleaning up foreground applications...");
            
            try
            {
                KillForegroundProcesses();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }

            Console.WriteLine("Cleanup complete. Exiting...");
        }

        /// <summary>
        /// Identifies and terminates all interactive foreground processes in the current session.
        /// </summary>
        static void KillForegroundProcesses()
        {
            Process currentProcess = Process.GetCurrentProcess();
            int currentSessionId = currentProcess.SessionId;
            int selfId = currentProcess.Id;

            // List of critical processes that should never be terminated to maintain system stability.
            // Explorer.exe and Dwm.exe are vital for the user interface.
            HashSet<string> criticalProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "explorer",    // Windows Shell
                "dwm",         // Desktop Window Manager
                "csrss",       // Client/Server Runtime Subsystem
                "smss",        // Session Manager
                "winlogon",    // Windows Logon Process
                "lsass",       // Local Security Authority
                "services",    // Services Control Manager
                "wininit",     // Windows Start-up
                "System",      // System Idle/Kernel
                "Idle"         // System Idle
            };

            // Get all processes running on the machine
            Process[] allProcesses = Process.GetProcesses();

            foreach (Process p in allProcesses)
            {
                try
                {
                    // 1. Must be in the current user session
                    // 2. Must have a Main Window Handle (Foreground/GUI app)
                    // 3. Must not be this killer application
                    // 4. Must not be in the critical system process list
                    if (p.SessionId == currentSessionId &&
                        p.MainWindowHandle != IntPtr.Zero &&
                        p.Id != selfId &&
                        !criticalProcesses.Contains(p.ProcessName))
                    {
                        string processName = p.ProcessName;
                        int processId = p.Id;

                        Console.Write($"Terminating: {processName} (PID: {processId})... ");
                        
                        p.Kill();
                        
                        // Briefly wait to verify termination
                        p.WaitForExit(1000);
                        
                        if (p.HasExited)
                        {
                            Console.WriteLine("Done.");
                        }
                        else
                        {
                            Console.WriteLine("Pending.");
                        }
                    }
                }
                catch (Win32Exception)
                {
                    // Occurs if the process is already exiting or access is denied
                    // Console.WriteLine($"Access denied for {p.ProcessName}.");
                }
                catch (InvalidOperationException)
                {
                    // Occurs if the process has already exited
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error killing {p.ProcessName}: {ex.Message}");
                }
                finally
                {
                    // Dispose of the process object to release resources
                    p.Dispose();
                }
            }
        }
    }
}