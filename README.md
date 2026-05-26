# Custom Windows Process Manager

This is a console-based Windows application built with C# and .NET that provides enhanced control over system processes. It allows users to list processes, identify high-resource consumers, and kill specific processes by ID or name.

**⚠️ IMPORTANT SAFETY WARNINGS ⚠️**

*   **Killing critical system processes (e.g., `explorer.exe`, `csrss.exe`) can lead to system instability, crashes, or require a reboot.** Use the killing features with extreme caution.
*   This application requires **Administrator privileges** to effectively list and terminate most system processes.

## Features

*   List all running processes with their ID, Name, Memory Usage, and whether they have a GUI.
*   Identify and list processes by estimated CPU usage (high to low).
*   List processes identified as "background" (those without a main window).
*   Terminate a process by its Process ID.
*   Terminate all processes with a given name.

## Getting Started

### Prerequisites

To build and run this application from source, you need:

*   [.NET SDK](https://dotnet.microsoft.com/download) (latest version recommended, e.g., .NET 8.0 SDK)
*   [Visual Studio Code](https://code.visualstudio.com/) (or Visual Studio)
*   [C# Dev Kit Extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) for VS Code (if using VS Code)

### Building and Running from Source (VS Code)

1.  **Clone the repository** (if this were on GitHub) or simply navigate to your project folder.
2.  **Open the project in VS Code:**
    ```bash
    cd ProcessManagerApp
    code .
    ```
3.  **Run in Debug Mode (F5) or without Debugging:**
    Open the integrated terminal (Ctrl+`) and run:
    ```bash
    dotnet run
    ```
    *Note: For full functionality, especially killing processes, you might need to launch VS Code itself as an Administrator or run the published executable as Administrator.*

### Building a Standalone Executable

To create a single `.exe` file that can be run on any 64-bit Windows machine (without needing the .NET runtime installed):

1.  Open the integrated terminal in VS Code (Ctrl+` ) and navigate to your project's root folder.
2.  Run the following command:
    ```bash
    dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
    ```
    This will create an optimized, self-contained executable.

### Running the Published Executable

1.  Navigate to the output folder: `ProcessManagerApp/bin/Release/netX.0/win-x64/publish/` (replace `netX.0` with your .NET version, e.g., `net8.0`).
2.  **Right-click on `ProcessManagerApp.exe` and select "Run as administrator".** This is essential for the application to function correctly.

## Usage

When running the application, you will be presented with a menu of options:

```
Welcome to the Custom Process Manager!
-------------------------------------

Choose an option:
1. List all processes
2. List processes by CPU Usage (High to Low)
3. List background processes
4. Kill a process by ID
5. Kill processes by Name
6. Exit
Enter your choice:
```

Follow the prompts to interact with the process manager.

## Contributing

If you'd like to contribute, please fork the repository and use a feature branch. Pull requests are welcome!

