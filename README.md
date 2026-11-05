# SnoozeIt

[![CI Build & Release](https://github.com/git-maniam/SnoozeIt/actions/workflows/ci.yml/badge.svg)](https://github.com/git-maniam/SnoozeIt/actions/workflows/ci.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%20x64-0078d4.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4.svg)](https://dotnet.microsoft.com/)

**SnoozeIt** is an autonomous process hibernation and working set eviction engine for Windows 11. It reclaims gigabytes of active RAM from idle background applications, browser processes, and developer workloads without terminating them, instantly waking them up in under 100 milliseconds the moment you focus on their window.

---

## Key Features

- **Kernel-Level Non-Destructive Suspension**: Utilizes Windows native `NtSuspendProcess` and `NtResumeProcess` APIs to pause background processes safely without killing state or dropping socket connections.
- **Aggressive Working Set Eviction**: Flushes private working sets to standby and pagefiles using `EmptyWorkingSet` and `SetProcessWorkingSetSizeEx`, reducing idle process memory footprints by up to 90%.
- **Instant Wake-on-Focus**: Hooks the Windows event pipeline via `EVENT_SYSTEM_FOREGROUND` to automatically thaw suspended applications before their windows finish redrawing ($< 100\text{ ms}$).
- **Core Audio / WASAPI Awareness**: Actively monitors the Windows Core Audio session enumerator (`IAudioSessionEnumerator`) to prevent hibernating apps playing music, video, or voice calls (e.g., Spotify, Teams, Discord).
- **Hierarchical Process Tree Grouping**: Groups multi-instance applications (such as Edge, Chrome, electron apps, and build workers) under unified expandable tree nodes with instance badges and aggregate memory counters.
- **Heuristic Scoring Engine**: Calculates real-time hibernation safety scores ($S_p \in [0, 100]$) based on CPU delta, memory pressure, window focus state, process age, and audio activity.
- **Secure Multi-User IPC**: High-performance JSON-RPC over Named Pipes (`\\.\pipe\SnoozeIt_IPC`) with DACL enforcement and kernel-verified caller session authentication (`GetNamedPipeClientSessionId`).
- **Modern Windows 11 Dashboard**: Built with WinUI 3, Windows App SDK, Mica Alt backdrops, Fluent 2 typography, and interactive status controls.

---

## Architecture & Project Structure

The solution consists of four projects organized under `SnoozeIt.slnx`:

```text
SnoozeIt/
├── SnoozeIt.Core/             # P/Invoke bindings, WASAPI audio detector, heuristic engine, safety filters
├── SnoozeIt.Core.Tests/       # 23 automated unit tests verifying filters, scores, and clamping rules
├── SnoozeIt.Service/          # LocalSystem Windows Service hosting the Named Pipe IPC server
├── SnoozeIt.Client/           # Self-contained WinUI 3 modern desktop dashboard application
├── SnoozeIt.Installer/        # WiX Toolset code-signed 64-bit MSI installer package
├── .github/workflows/ci.yml   # Full CI/CD pipeline: build, test, WiX harvesting, and Authenticode signing
└── demo.ps1                   # 60-second end-to-end automated live demonstration script
```

---

## Prerequisites

- **Operating System**: Windows 11 (build 22000 or newer) or Windows 10 x64.
- **Permissions**: Administrator privileges are required to run the Windows Service and manage arbitrary process working sets.
- **Developer Prerequisites** (for building from source):
  - [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
  - [WiX Toolset v3.14+](https://wixtoolset.org/) (for building the MSI installer)
  - Visual Studio 2026 / 2022 or VS Code with C# Dev Kit

---

## Installation

### Option 1: Code-Signed MSI Installer (Recommended)

1. Download the latest `SnoozeItInstaller.msi` from GitHub Releases or from the `installer/` directory.
2. Run the installer with administrator privileges:

   ```cmd
   msiexec /i installer\SnoozeItInstaller.msi
   ```

   Or install silently via elevated PowerShell/Command Prompt:

   ```powershell
   Start-Process msiexec.exe -ArgumentList '/i "installer\SnoozeItInstaller.msi" /qn' -Verb RunAs -Wait
   ```

3. The installer registers the `SnoozeItService` Windows Service to start automatically and installs the `SnoozeIt` application shortcut to your Start Menu.

### Option 2: Build from Source

Clone the repository and build the Release configuration:

```powershell
git clone https://github.com/git-maniam/SnoozeIt.git
cd SnoozeIt

# Restore and build the complete solution
dotnet build SnoozeIt.slnx -c Release

# Run the automated unit tests (23/23 tests)
dotnet test SnoozeIt.slnx -c Release --no-build
```

---

## Usage Instructions

### 1. Running the 60-Second Automated Demo

The repository includes a PowerShell demo script (`demo.ps1`) that spins up synthetic memory-heavy workloads, demonstrates live RAM reclamation, and verifies instant thawing.

> [!IMPORTANT]
> Always execute `demo.ps1` from the root directory of the repository or supply its absolute path.

```powershell
# Open an elevated PowerShell terminal and navigate to the project directory:
cd D:\Git-Projects\SnoozeIt

# Execute the demonstration script:
powershell -ExecutionPolicy Bypass -File .\demo.ps1
```

**What the demo performs:**

1. Launches three synthetic background worker processes allocating ~150 MB of memory each.
2. Checks connectivity with the `SnoozeItService` IPC engine.
3. Queries live telemetry and calculates snoozeability scores for the background processes.
4. Suspends and evicts working sets, showcasing instant memory reclamation.
5. Thaws the processes and cleanly tears down the synthetic workloads.

---

### 2. Launching the WinUI 3 Dashboard

You can start the desktop UI client using either:

- **Start Menu**: Press the Windows Key, type **SnoozeIt**, and press Enter.
- **Command Line**:

  ```powershell
  # Launch the installed desktop client
  Start-Process "C:\Program Files\SnoozeIt\SnoozeIt.Client.exe"
  ```

- **Local Development Run**:

  ```powershell
  # Launch from the build output directory
  Set-Location "D:\Git-Projects\SnoozeIt\SnoozeIt.Client\bin\Release\net10.0-windows10.0.26100.0\win-x64"
  .\SnoozeIt.Client.exe
  ```

---

### 3. Using the Dashboard

Once opened, the dashboard displays live memory metrics and a grouped tree of running processes:

```text
┌────────────────────────────────────────────────────────────────────────┐
│ RAM Reclaimed: 1.4 GB Reclaimed   │ Snoozed: 5 Apps   │ Engine: Active │
└────────────────────────────────────────────────────────────────────────┘
  [⚡ Snooze All Safe]   [🔍 Filter Processes...]
──────────────────────────────────────────────────────────────────────────
  ▼ msedge (6 instances)     Total RAM: 420.5 MB   Score: 82/100  [Snooze]
      ├─ PID 12404  |  CPU: 0.0%  |  RAM: 85.2 MB   |  Score: 82  [Snooze]
      ├─ PID 13180  |  CPU: 0.0%  |  RAM: 72.1 MB   |  Score: 78  [Snooze]
      └─ ...
  ▶ Code (4 instances)       Total RAM: 680.1 MB   Score: 65/100  [Snooze]
```

#### Controls & Actions

- **⚡ Snooze All Safe**: Scans all active processes and hibernates every safe candidate ($S_p \ge 70$) in a single click.
- **Process Group Expander**: Click any group header to expand or collapse child processes.
- **Group Snooze / Thaw**: Click the group action button to suspend or wake all instances in that process family simultaneously.
- **Individual Process Controls**:
  - **💤 Snooze**: Immediately suspends process threads and flushes its working set memory pages.
  - **☀️ Thaw**: Resumes thread execution and restores the application to normal operation.
- **Auto-Thaw on Focus**: Switch windows using `Alt+Tab` or click on any snoozed window. The foreground hook detects the focus transition and wakes the target process instantly.

---

## Safety Filters & Heuristics

To prevent system instability or data loss, SnoozeIt applies strict heuristic safeguards:

| Protection Criterion | Behavior |
|---|---|
| **Critical System Processes** | Protected (`csrss.exe`, `lsass.exe`, `explorer.exe`, `services.exe`, etc. are blocked). |
| **Foreground Application** | Protected (the active window is never snoozed; score forced to `0`). |
| **Active Core Audio** | Protected (processes with active audio sessions receive a $-50$ penalty). |
| **Process Age** | Processes alive for less than 3 minutes are penalized to allow startup tasks to complete. |
| **High CPU Delta** | Busy processes ($> 2\%$ CPU utilization) receive significant penalties. |
| **Multi-Session Isolation** | Standard user clients can only suspend processes belonging to their own logon session. |

---

## Service Management & Troubleshooting

### Verifying Service Status

```powershell
# Check Windows Service status
Get-Service SnoozeItService

# Start or stop the service
Start-Service SnoozeItService
Stop-Service SnoozeItService
```

### Direct Mode Fallback

If `SnoozeIt.Client.exe` is run on a workstation where `SnoozeItService` is not installed, it automatically switches to **Direct User Mode**. In this mode, the client performs process hibernation directly within the user's security token and desktop session.

---

## Contributing & Development

```powershell
# Run static analysis and formatting checks
dotnet build SnoozeIt.slnx -c Release

# Execute test suite
dotnet test SnoozeIt.slnx -c Release --no-build
```

Automated GitHub Actions workflows enforce build passing, test execution, and WiX installer compilation on every push and pull request.
