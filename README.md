# SnoozeIt

[![CI Build & Release](https://github.com/git-maniam/SnoozeIt/actions/workflows/ci.yml/badge.svg)](https://github.com/git-maniam/SnoozeIt/actions/workflows/ci.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%20x64-0078d4.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4.svg)](https://dotnet.microsoft.com/)

**SnoozeIt** is an autonomous process hibernation and working set eviction engine for Windows 11. It reclaims gigabytes of active RAM from idle background applications, browser processes, and developer workloads without terminating them, instantly waking them up in under 100 milliseconds the moment you focus on their window.

---

## Why SnoozeIt? (The Story & Motivation)

SnoozeIt is provided free of charge for personal and commercial use, without warranties or guarantees of any kind.

As a developer, I created this tool to help fellow developers. I experienced high RAM usage during builds, while RAM prices have increased significantly due to the AI boom. Not every developer can afford to upgrade their machine regularly.

SnoozeIt helps address this problem by intelligently pausing services such as Docker Desktop during build phases or other heavy services during unit-test runs, helping conserve RAM and system resources.

Developers are already facing increasing competition from AI. By making small, useful tools like this freely available, we can support and strengthen our developer community.

You can find the full source code at [github.com/git-maniam/SnoozeIt](https://github.com/git-maniam/SnoozeIt). Feel free to use it, fork it, modify it, or do whatever you like with it—but please continue sharing it freely with others.

---

## Key Features

- **Kernel-Level Non-Destructive Suspension**: Utilizes Windows native `NtSuspendProcess` and `NtResumeProcess` APIs to pause background processes safely without killing state or dropping socket connections.
- **Aggressive Working Set Eviction**: Flushes private working sets to standby and pagefiles using `EmptyWorkingSet` and `SetProcessWorkingSetSizeEx`, reducing idle process memory footprints by up to 90%.
- **Instant Wake-on-Focus**: Hooks the Windows event pipeline via `EVENT_SYSTEM_FOREGROUND` to automatically thaw suspended applications before their windows finish redrawing ($< 100\text{ ms}$).
- **Core Audio / WASAPI Awareness**: Actively monitors the Windows Core Audio session enumerator (`IAudioSessionEnumerator`) to prevent hibernating apps playing music, video, or voice calls (e.g., Spotify, Teams, Discord).
- **Hierarchical Process Tree Grouping**: Groups multi-instance applications (such as Edge, Chrome, electron apps, and build workers) under unified expandable tree nodes with instance badges and aggregate memory counters.
- **Top Bar RAM Telemetry (30s Auto-Refresh)**: Tracks live Total Installed RAM, active Used RAM, and dynamic Reclaimed RAM every 30 seconds; automatically decrements reclaimed RAM whenever hibernated services awaken.
- **Heuristic Scoring Engine**: Calculates real-time hibernation safety scores ($S_p \in [0, 100]$) based on CPU delta, memory pressure, window focus state, process age, and audio activity.
- **Secure Multi-User IPC**: High-performance JSON-RPC over Named Pipes (`\\.\pipe\SnoozeIt_IPC`) with DACL enforcement and kernel-verified caller session authentication (`GetNamedPipeClientSessionId`).
- **Modern Windows 11 Dashboard**: Built with WinUI 3, Windows App SDK, Mica Alt backdrops, Fluent 2 typography, About splash modal, and interactive status controls.

---

## Architecture & Project Structure

The solution consists of four projects organized under `SnoozeIt.slnx`:

```text
SnoozeIt/
├── SnoozeIt.Core/             # P/Invoke bindings, WASAPI audio detector, heuristic engine, safety filters
├── SnoozeIt.Core.Tests/       # 24 automated unit tests verifying filters, scores, memory stats, and clamping
├── SnoozeIt.Service/          # LocalSystem Windows Service hosting the Named Pipe IPC server
├── SnoozeIt.Client/           # Self-contained WinUI 3 modern desktop dashboard application
├── SnoozeIt.Installer/        # WiX Toolset code-signed 64-bit MSI installer package
├── .github/workflows/ci.yml   # Full CI/CD pipeline: build, test, WiX harvesting, and Authenticode signing
├── setup.ps1                  # 1-click automated uninstaller, cert trust, and fresh MSI installer
├── About-SnoozeIt.md          # Project mission statement, community ethos, and author note
└── demo.ps1                   # 60-second end-to-end automated live demonstration script
```

---

## Prerequisites

- **Operating System**: Windows 11 (build 22000 or newer) or Windows 10 x64.
- **Permissions**: Administrator privileges are required to run the Windows Service and manage arbitrary process working sets.
- **Developer Prerequisites** (for building from source):
  - [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
  - [WiX Toolset v4 / v3.14+](https://wixtoolset.org/) (for building the MSI installer)
  - Visual Studio 2026 / 2022 or VS Code with C# Dev Kit

---

## Installation

### Option 1: Automated 1-Click Setup Script (Recommended)

Run the bundled [`setup.ps1`](setup.ps1) script in an elevated (Administrator) PowerShell terminal:

```powershell
# Navigate to the repository directory and run setup:
cd D:\Git-Projects\SnoozeIt
.\setup.ps1
```

The script will automatically:
1. Gracefully close any active SnoozeIt Client processes.
2. Stop the background `SnoozeItService`.
3. Cleanly uninstall any prior MSI version.
4. Add the code-signing certificate to your machine's Trusted Root Certification Authorities store.
5. Silently install the fresh `SnoozeItInstaller.msi`.
6. Start the `SnoozeItService` and launch the WinUI 3 Client application.

### Option 2: Code-Signed MSI Installer

1. Download the latest `SnoozeItInstaller.msi` from GitHub Releases or from the `installer/` directory.
2. Run the installer with administrator privileges:

   ```cmd
   msiexec /i installer\SnoozeItInstaller.msi
   ```

   Or install silently via elevated PowerShell:

   ```powershell
   Start-Process msiexec.exe -ArgumentList '/i "installer\SnoozeItInstaller.msi" /qn' -Verb RunAs -Wait
   ```

3. The installer registers the `SnoozeItService` Windows Service to start automatically and installs the `SnoozeIt` application shortcut to your Start Menu.

### Option 3: Build from Source

Clone the repository and build the Release configuration:

```powershell
git clone https://github.com/git-maniam/SnoozeIt.git
cd SnoozeIt

# Restore and build the complete solution
dotnet build SnoozeIt.slnx -c Release

# Run the automated unit tests (24/24 tests)
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

---

### 2. Launching the WinUI 3 Dashboard

You can start the desktop UI client using either:

- **Start Menu**: Press the Windows Key, type **SnoozeIt**, and press Enter.
- **Command Line**:

  ```powershell
  # Launch the installed desktop client
  Start-Process "C:\Program Files\SnoozeIt\SnoozeIt.Client.exe"
  ```

---

### 3. Using the Dashboard

Once opened, the dashboard displays live memory metrics and a grouped tree of running processes:

```text
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ RAM Usage                                                                              │
│ Total RAM: 15.9 GB │ Used RAM: 9.8 GB │ Reclaimed RAM: 450.2 MB │ Snoozed: 3 Apps     │
│ Execution Engine: Privileged Service Mode                 [About-SnoozeIt]             │
│                                                           [⚡ Snooze All Safe]          │
└────────────────────────────────────────────────────────────────────────────────────────┘
  [🔍 Search active processes or service groups...]   [🔄 Refresh]
──────────────────────────────────────────────────────────────────────────────────────────
  ▼ 💤 Snoozed Services (Pinned Top Level)     Total RAM: 240.2 MB  [Snooze & Wake 🔘]
      ├─ Docker Desktop (PID 3812) |  CPU: 0.0%  |  RAM: 1.8 GB    [Snooze & Wake 🔘]
      ├─ msedge (PID 1420)         |  CPU: 0.0%  |  RAM: 120.1 MB  [Snooze & Wake 🔘]
      └─ Code (PID 5892)           |  CPU: 0.0%  |  RAM: 120.1 MB  [Snooze & Wake 🔘]
  ▼ msedge (5 active instances)                Total RAM: 300.4 MB  [Snooze & Wake ⚪]
      ├─ PID 12404  |  CPU: 0.0%  |  RAM: 85.2 MB   |  Score: 82   [Snooze & Wake ⚪]
      └─ ...
  ▼ system (Critical OS Process)               Total RAM: 150.0 MB  [Snooze & Wake ⚪ (Disabled)]
                                                                    ⛔ Unsafe to Snooze
```

#### Controls & Actions

- **About-SnoozeIt Link**: Situated right above the "⚡ Snooze All Safe" button. Opens a splash dialog presenting the tool's mission statement, developer backstory, community ethos, and clickable GitHub link.
- **⚡ Snooze All Safe**: Scans all active processes and hibernates every safe candidate ($S_p \ge 70$) in a single click.
- **Snooze & Wake Slider (`ToggleSwitch`)**:
  - Slide or click the toggle on any process card or group header to instantly hibernate (On / `Snooze`) or restore (Off / `Wake`) the workload.
  - Dynamically updates thread suspension state and working set memory pages in real time.
- **Pinned 'Snoozed Services' Section**:
  - Whenever a service or process is snoozed, it automatically moves into the dedicated **Snoozed Services** high-level folder pinned at the top of the tree view.
  - Thawing the workload returns it immediately to its regular process group below.
- **'Unsafe to Snooze' Protection**:
  - Critical Windows operating system binaries (e.g., `smss.exe`, `csrss.exe`, `wininit.exe`, `services.exe`, `lsass.exe`, `svchost.exe`) and active foreground windows have their slider permanently locked and greyed out with a red **Unsafe to Snooze** badge underneath to guarantee OS stability. All other applications and user services can be safely snoozed.
- **Auto-Thaw on Focus**: Switch windows using `Alt+Tab` or click on any snoozed window. The foreground hook detects the focus transition and wakes the target process in $< 100\text{ ms}$.

---

## Safety Filters & Heuristics

To prevent system instability or data loss, SnoozeIt applies strict heuristic safeguards:

| Protection Criterion | Behavior |
|---|---|
| **Critical System Processes** | Protected (`csrss.exe`, `lsass.exe`, `services.exe`, `smss.exe`, PID $\le 4$). |
| **Foreground Application** | Protected (the active window is never snoozed; score forced to `0`). |
| **Active Core Audio** | Protected (processes with active audio sessions receive a $-50$ penalty). |
| **Process Age** | Processes alive for less than 3 minutes are penalized to allow startup tasks to complete. |
| **High CPU Delta** | Busy processes ($> 2\%$ CPU utilization) receive significant penalties. |
| **Multi-Session Isolation** | Standard user clients can only suspend processes belonging to their own logon session. |

---

## About the Author & Giving Back

I am a developer at heart and a Principal Technical Program Manager by profession, based in Bangalore, India.

I will never ask for money or donations for tools like this. If you would like to do something meaningful, consider donating to a charity of your choice—for example, one that helps children learn new skills or supports elderly people in need. The choice is entirely yours.

---

## Contributing & Development

```powershell
# Run static analysis and formatting checks
dotnet build SnoozeIt.slnx -c Release

# Execute test suite (all 24 tests passing)
dotnet test SnoozeIt.slnx -c Release --no-build
```

Automated GitHub Actions workflows enforce build passing, test execution, and WiX installer compilation on every push and pull request.
