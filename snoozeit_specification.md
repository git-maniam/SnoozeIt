# SnoozeIt: Autonomous Process Hibernation & Working Set Eviction Engine
## Software Architecture & Implementation Specification (v1.0.0)
**Target Platform:** Windows 10 (Build 19041+) / Windows 11 (All Builds)  
**Framework:** .NET 9 (C# 13, Native AOT-ready)  
**Execution Context:** Dual-Mode (Privileged Windows Service + Low-Privilege Per-User Desktop UI)

---

## 1. Executive Summary & Value Proposition

Standard operating systems allow idle background processes (Electron runtimes, idle IDE instances, background browser tabs, game launchers) to retain gigabytes of committed physical RAM and consume continuous background CPU cycles. 

**SnoozeIt** is an intelligent, low-overhead resource hibernation engine that:
1. Detects inactive background workloads using hardware, network, audio, and UI telemetry.
2. Atomically halts user-mode thread scheduling via low-level Windows NT kernel APIs (`NtSuspendProcess`).
3. Flushes physical RAM working sets to the Windows paging subsystem (`EmptyWorkingSet`).
4. Instantly wakes ("thaws") the target on demand or upon window focus in $< 100\text{ ms}$ with zero state loss.
5. Runs safely in both standard user and elevated administrator environments across multi-user sessions.

---

## 2. Multi-User & Privilege Architecture

A major challenge in process management on Windows is that non-administrative users cannot inspect or manipulate processes owned by other sessions or elevated tokens (`EPERM` / `STATUS_ACCESS_DENIED`). SnoozeIt solves this via a **split-daemon architecture**:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        SESSION 0 (System Space)                        │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ SnoozeIt.Service (Windows Service - LocalSystem)                 │  │
│  │  - SeDebugPrivilege enabled                                      │  │
│  │  - Native NT kernel invoker (NtSuspendProcess, EmptyWorkingSet)  │  │
│  │  - Named Pipe Server: \\.\pipe\SnoozeIt_IPC                      │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────▲────────────────────────────────────┘
                                    │ IPC (DACL-Secured Named Pipe)
┌───────────────────────────────────▼────────────────────────────────────┐
│                    USER SESSION (Interactive Desktop)                  │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ SnoozeIt.Client (WPF / Modern Fluent UI + Taskbar Tray)           │  │
│  │  - Standard User Token (Medium Integrity)                        │  │
│  │  - WinEvent Hook Listener (EVENT_SYSTEM_FOREGROUND)              │  │
│  │  - Smart Recommendation Heuristic Engine                         │  │
│  │  - Fallback: Direct Local Mode (if Service is not installed)     │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────┘
```

### 2.1 Privilege Modes
* **Installed Mode (Enterprise/Multi-User):** `SnoozeIt.Service` runs as `NT AUTHORITY\SYSTEM`. Any standard (non-admin) user can run `SnoozeIt.Client`. The service enforces access control: a non-admin user can only issue suspend/resume commands for processes originating from their own `SessionId`.
* **Standalone / Portable Mode:** If the Windows Service is not detected, `SnoozeIt.Client` operates in standalone user-mode. It can manage all processes running within the current user's security token and gracefully badges privileged processes with a "Requires Admin Service" lock icon.

---

## 3. Core Engine & Win32/NT Low-Level API Specifications

### 3.1 P/Invoke Method Signatures (To be generated via `Microsoft.Windows.CsWin32` or manual P/Invoke)

```csharp
namespace SnoozeIt.Core.Native;

using System;
using System.Runtime.InteropServices;

internal static partial class NativeMethods
{
    // --- Kernel Suspension & Thawing (ntdll.dll) ---
    [LibraryImport("ntdll.dll")]
    public static partial int NtSuspendProcess(IntPtr processHandle);

    [LibraryImport("ntdll.dll")]
    public static partial int NtResumeProcess(IntPtr processHandle);

    // --- Memory Eviction (psapi.dll / kernel32.dll) ---
    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyWorkingSet(IntPtr hProcess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWorkingSetSizeEx(
        IntPtr hProcess,
        IntPtr dwMinimumWorkingSetSize,
        IntPtr dwMaximumWorkingSetSize,
        uint flags);

    public const uint QUOTA_LIMITS_HARDWS_MIN_ENABLE = 0x00000001;
    public const uint QUOTA_LIMITS_HARDWS_MIN_DISABLE = 0x00000002;

    // --- Process Inspection & Security (kernel32.dll / advapi32.dll) ---
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint processAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int processId);

    public const uint PROCESS_SUSPEND_RESUME = 0x0800;
    public const uint PROCESS_SET_QUOTA = 0x0100;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // --- Window Focus Tracking (user32.dll) ---
    public delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsHungAppWindow(IntPtr hWnd);
}
```

### 3.2 Atomic Eviction Execution Flow
To snooze a process without data corruption:
1. **Safety Check:** Query if the process has hung windows via `IsHungAppWindow`. If hung, abort.
2. **Atomic Pause:** Call `NtSuspendProcess(hProcess)`. Verify the return value is `0` (`STATUS_SUCCESS`).
3. **Paging Eviction:** 
   * Execute `EmptyWorkingSet(hProcess)`.
   * Apply `SetProcessWorkingSetSizeEx(hProcess, (IntPtr)(-1), (IntPtr)(-1), QUOTA_LIMITS_HARDWS_MIN_ENABLE)`.
4. **State Transition:** Update internal state tracker to `Snoozed`. Record memory reclaimed ($\text{PhysicalRAM}_{\text{before}} - \text{PhysicalRAM}_{\text{after}}$).

---

## 4. Smart Process Scoring & Recommendation Engine

The engine computes a **Snoozeability Score** ($S \in [0, 100]$) for every running process every $5\text{ seconds}$.

### 4.1 Scoring Formula

$$S = \text{Clamp}\left( W_{\text{idle}} + W_{\text{mem}} - P_{\text{audio}} - P_{\text{io}} - P_{\text{net}} - P_{\text{focus}} - P_{\text{critical}},\, 0,\, 100 \right)$$

Where:
* **$W_{\text{idle}}$ (Inactivity Weight, Max 40 pts):** Points increase monotonically with time elapsed since the process was the foreground window ($5\text{ pts}$ per minute, capped at 40).
* **$W_{\text{mem}}$ (RAM Consumption Weight, Max 30 pts):** $1\text{ pt}$ per $50\text{ MB}$ of Private Working Set, capped at 30.
* **$P_{\text{audio}}$ (Active Audio Penalty, 80 pts):** If the process is currently streaming audio via Windows Core Audio (WASAPI), $P_{\text{audio}} = 80$. This prevents muting Spotify, YouTube tabs, or Discord calls.
* **$P_{\text{io}}$ (Disk I/O Penalty, 50 pts):** If disk write throughput exceeds $500\text{ KB/s}$ (active compile, render, or download), $P_{\text{io}} = 50$.
* **$P_{\text{net}}$ (Network Activity Penalty, 40 pts):** If open active TCP sockets with $> 50\text{ KB/s}$ transfer rate are detected, $P_{\text{net}} = 40$.
* **$P_{\text{focus}}$ (Focus Recency Penalty, 90 pts):** If the process was active within the last $60\text{ seconds}$, $P_{\text{focus}} = 90$.
* **$P_{\text{critical}}$ (Safety Blacklist Penalty, $\infty$ pts):** Hard set to 0 score if the process is critical.

### 4.2 Recommendation Categories
* **Score $\ge 70$ (Tier 1: Safe to Snooze):** Highlighted in green. Idle Electron apps, closed background browsers, background store clients (Steam, Epic, Battle.net).
* **Score $40 - 69$ (Tier 2: Snooze with Caution):** Idle IDEs without active build tasks, office tools with saved states.
* **Score $< 40$ (Tier 3: Do Not Snooze):** Active workloads, media players, communications software.

### 4.3 Active Audio Detection (WASAPI / `IAudioSessionEnumerator`)
To prevent snoozing apps playing background audio, use the Windows Core Audio COM API:
* Interrogate `IMMDeviceEnumerator` $\to$ default multimedia device.
* Retrieve `IAudioSessionManager2`.
* Call `GetSessionEnumerator()` and loop through `IAudioSessionControl2`.
* Check `GetState()` for `AudioSessionStateActive`. If true, extract the `ProcessId`.

### 4.4 The Critical Safeguard Whitelist (Hardcoded)
Never allow suspension of the following binaries under any condition:
```csharp
public static readonly FrozenSet<string> BlacklistedBinaries = new[]
{
    "system", "idle", "smss.exe", "csrss.exe", "wininit.exe", "services.exe",
    "lsass.exe", "svchost.exe", "dwm.exe", "explorer.exe", "taskhostw.exe",
    "sihost.exe", "fontdrvhost.exe", "runtimebroker.exe", "shellexperiencehost.exe",
    "searchindexer.exe", "securityhealthservice.exe", "msmpeng.exe",
    "snoozeit.client.exe", "snoozeit.service.exe"
}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
```

---

## 5. IPC Protocol & Data Contracts

The Service and Client communicate over a secure Named Pipe: `\\.\pipe\SnoozeIt_IPC`.
Pipe security: Configured with a `PipeSecurity` descriptor allowing `LocalSystem` (Full Control) and `AuthenticatedUsers` (Read/Write only).

### 5.1 JSON-RPC Payload Contracts

#### Suspend Request (`client -> service`)
```json
{
  "jsonrpc": "2.0",
  "id": "uuid-1234",
  "method": "Process.Suspend",
  "params": {
    "processId": 14208,
    "trimWorkingSet": true,
    "originatingSessionId": 1
  }
}
```

#### Resume Request (`client -> service`)
```json
{
  "jsonrpc": "2.0",
  "id": "uuid-1235",
  "method": "Process.Resume",
  "params": {
    "processId": 14208,
    "originatingSessionId": 1
  }
}
```

#### Telemetry Event (`service -> client`)
```json
{
  "jsonrpc": "2.0",
  "method": "Telemetry.ProcessReclaimed",
  "params": {
    "processId": 14208,
    "processName": "Slack.exe",
    "reclaimedBytes": 482344960,
    "elapsedMicroseconds": 4120
  }
}
```

---

## 6. Complete Solution & Project Directory Structure

```
SnoozeIt/
├── SnoozeIt.sln
├── Directory.Build.props                     # Common compiler flags (Nullable, LangVersion 13, NativeAOT)
├── src/
│   ├── SnoozeIt.Core/                        # Shared domain logic, Win32 bindings, Scoring Models
│   │   ├── Native/
│   │   │   ├── NativeMethods.cs              # CsWin32 / Manual P-Invoke declarations
│   │   │   ├── WinEventHook.cs               # Event listener for window focus changes
│   │   │   └── AudioDetector.cs              # WASAPI audio session inspector
│   │   ├── Engine/
│   │   │   ├── ProcessController.cs          # Suspend/Resume/Evict core execution
│   │   │   ├── HeuristicEngine.cs            # Score calculation algorithm
│   │   │   └── SafetyFilter.cs               # Whitelist & PPL checks
│   │   └── Models/
│   │       ├── ProcessInfoModel.cs           # Memory, CPU, Snooze status data classes
│   │       └── SnoozeRecommendation.cs       # Recommendation tiers & breakdown
│   │
│   ├── SnoozeIt.Service/                     # Windows Service (SYSTEM daemon)
│   │   ├── Worker.cs                         # BackgroundService loop
│   │   ├── Ipc/
│   │   │   ├── PipeServer.cs                 # Named pipe listener
│   │   │   └── RequestDispatcher.cs          # Command validation & execution
│   │   └── Program.cs                        # Host builder & WindowsService integration
│   │
│   ├── SnoozeIt.Client/                      # WPF / Wpf.Ui Modern Application
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── Views/
│   │   │   ├── MainWindow.xaml               # Mica-backed Fluent dashboard
│   │   │   └── TrayIconView.xaml             # H.NotifyIcon system tray setup
│   │   ├── ViewModels/
│   │   │   ├── MainViewModel.cs              # Live process list, filter, sorting
│   │   │   └── ProcessRowViewModel.cs        # Individual process card & score
│   │   ├── Services/
│   │   │   ├── IpcClient.cs                  # Client-side named pipe client
│   │   │   └── WindowFocusWatcher.cs         # Auto-thaw trigger upon clicking app
│   │   └── Program.cs
│   │
│   └── SnoozeIt.Installer/                   # WiX Toolset v4 installer (Installs Service & App)
└── tests/
    ├── SnoozeIt.Core.Tests/
    └── SnoozeIt.Engine.IntegrationTests/
```

---

## 7. UI/UX Specification (Fluent 2 Design)

* **Design Standard:** Windows 11 Fluent 2 with **Mica Alt** backdrop material, hardware-accelerated animations, and Dark/Light auto-theming.
* **Header Statistics Banner:**
  * **Saved RAM:** Real-time counter of total freed RAM (e.g., `3.42 GB Reclaimed`).
  * **CPU Saved:** Background CPU time spared.
  * **Snoozed Apps Count:** Active count of paused binaries.
* **Process List View:**
  * Displays: Process Icon, Friendly Name, PID, Current RAM, Potential Savings, and **Snoozeability Gauge** (Circular color-coded meter: 0-100).
  * Quick Actions: [Snooze Now], [Whitelist Forever], [Auto-Snooze Toggle].
* **System Tray & Instant Thaw:**
  * Minimize to system tray on close.
  * Clicking any snoozed window on the Windows Taskbar automatically fires `EVENT_SYSTEM_FOREGROUND`, sending an instant `Process.Resume` command. The user experiences zero friction—the app unfreezes seamlessly without opening SnoozeIt.

---

## 8. Phase-by-Phase Autonomous Implementation Plan

This breakdown is structured for an AI agent working in sequence:

### Phase 1: Core Native Bindings & Process Engine (`SnoozeIt.Core`)
1. Create `.sln` and `.csproj` targeting `net9.0-windows10.0.19041.0`.
2. Implement `NativeMethods.cs` using `[LibraryImport]` for `NtSuspendProcess`, `NtResumeProcess`, `EmptyWorkingSet`, and `SetProcessWorkingSetSizeEx`.
3. Implement `SafetyFilter.cs` with the complete hardcoded whitelist and basic checks (refuse PID $\le 4$).
4. Implement `ProcessController.cs` providing:
   * `Result<long> SuspendAndTrim(int processId)`
   * `Result Resume(int processId)`
5. Write unit tests launching a sacrificial process (`notepad.exe`), asserting thread suspension state and working set reduction of $> 80\%$.

### Phase 2: Telemetry & Smart Heuristic Engine
1. Implement `AudioDetector.cs` wrapping Windows Core Audio `IAudioSessionEnumerator` via COM interop to detect processes with active audio streams.
2. Implement memory and CPU sampling via `System.Diagnostics.Process` and performance counters.
3. Implement `HeuristicEngine.cs` with the scoring equation specified in Section 4.1.
4. Add diagnostic tests asserting that active media players receive a score $< 20$ while idle browsers score $> 70$.

### Phase 3: Named Pipe Service & IPC Layer (`SnoozeIt.Service`)
1. Scaffold `SnoozeIt.Service` using `Microsoft.Extensions.Hosting.WindowsServices`.
2. Implement `PipeServer.cs` with asynchronous NamedPipe listening (`NamedPipeServerStream`) and customized `PipeSecurity` (World Read/Write, System Full Control).
3. Implement session validation: extract the calling client's token session and verify matches the target PID's session ID (allowing admin override).
4. Implement fallback handling: if the service is stopped or uninstalled, allow the client to fall back to in-process execution for same-user processes.

### Phase 4: Modern Desktop Client & Focus Automation (`SnoozeIt.Client`)
1. Scaffold modern desktop application using WPF with `Wpf.Ui` (or WinUI 3) and `CommunityToolkit.Mvvm`.
2. Connect `WindowFocusWatcher.cs` to `SetWinEventHook` monitoring `EVENT_SYSTEM_FOREGROUND`. When a snoozed process gains focus, automatically thaw it immediately.
3. Build the primary dashboard with search/filtering, real-time memory metrics, and one-click "Snooze All Recommended" button.
4. Add tray icon integration with right-click menu ("Thaw All", "Settings", "Exit").

### Phase 5: Packaging, Packaging Scripts & Demo Setup
1. Create a PowerShell build script (`build.ps1`) that:
   * Compiles the solution in Release mode.
   * Publishes the client and service binaries.
   * Registers `SnoozeIt.Service` using `sc.exe create SnoozeItService binPath=... start=auto`.
2. Provide a mock test script that launches three simulated heavy workloads (synthetic Electron apps / memory hog test scripts) to deliver an instant, deterministic 60-second competition live demo.

## 9. Development Environment Setup

| Component | Version / Requirement | Installation Guidance |
|-----------|-----------------------|-----------------------|
| Windows OS | Windows 11 (any build) | Ensure latest cumulative updates installed |
| .NET SDK | .NET 9 (C# 13) | Install via https://dotnet.microsoft.com/download/dotnet/9.0 (includes Windows targeting packs) |
| Visual Studio | VS 2022 17.9+ (or later) with **Desktop development with C#** workload | Use the Visual Studio Installer ➜ Modify ➜ Workloads ➜ "Desktop development with C#". This pulls in the Windows 11 SDK and MSBuild support |
| Windows 11 SDK | 10.0.22621.0 (or newer) | Comes with the VS workload; can also be installed separately from the Windows SDK archive |
| WiX Toolset | v4.x (latest) | Download from https://wixtoolset.org/releases/. Add the WiX extension to VS for project templates |
| NuGet Packages | `Wpf.Ui`, `CommunityToolkit.Mvvm`, `Microsoft.Windows.CsWin32`, `System.Drawing.Common`, `Newtonsoft.Json` (or `System.Text.Json`) | `dotnet add package <pkg>` or via VS NuGet Manager |
| Optional Tools | PowerShell 7+, Git for source control, GitHub CLI (optional) | Install via winget or Chocolatey |

> **Tip**: After installing the .NET SDK, restart any open terminals or IDEs to ensure the `dotnet` command is on the PATH.

## 10. Execution Plan & Timeline

The implementation is broken into incremental phases that can be developed, unit‑tested, and integrated independently. Below is a high‑level Gantt‑style timeline (working days, assuming a single developer at ~6 h/day).

| Phase | Duration | Core Deliverables | Verification |
|-------|----------|-------------------|--------------|
| **Phase 1** – Core Native Bindings & Engine | 4 days | • Solution & project files<br>• `NativeMethods.cs` with `[LibraryImport]` wrappers<br>• `SafetyFilter.cs` (blacklist & PID guard)<br>• `ProcessController.cs` (Suspend/Resume/Evict) | Unit tests using a sacrificial process (`notepad.exe`) confirm suspension state and ≥80 % working‑set reduction |
| **Phase 2** – Telemetry & Heuristic Engine | 5 days | • `AudioDetector.cs` (COM interop for WASAPI)<br>• Memory/CPU sampling logic<br>• `HeuristicEngine.cs` implementing the scoring formula<br>• Data models (`ProcessInfoModel`, `SnoozeRecommendation`) | Integration tests simulate active audio, heavy I/O, and idle workloads; assert score categories match expectations |
| **Phase 3** – Service & IPC Layer | 4 days | • `SnoozeIt.Service` project scaffolding (Windows Service host)<br>• Named pipe server (`PipeServer.cs`) with security descriptor<br>• `RequestDispatcher.cs` handling JSON‑RPC suspend/resume calls | End‑to‑end test: client sends suspend request → service invokes `ProcessController`; client receives telemetry JSON |
| **Phase 4** – Desktop Client UI | 6 days | • WPF project using `Wpf.Ui` and `CommunityToolkit.Mvvm`
• Dashboard XAML (process list, RAM counters, snoozeability gauge)
• Tray icon integration (`H.NotifyIcon`)
• `WindowFocusWatcher.cs` (WinEvent hook for auto‑thaw) | Manual UI testing: selecting a process, clicking *Snooze Now* pauses it; focusing the window resumes it instantly |
| **Phase 5** – Packaging & Demo Scripts | 3 days | • PowerShell `build.ps1` (restore, build, publish, service registration)<br>• WiX installer project (`SnoozeIt.Installer`) creating MSI that registers the service and shortcuts
• Demo script launching synthetic workloads & auto‑run of the UI | Install on a clean Windows 11 VM, verify service starts, client displays processes, demo script shows RAM reclaimed stats |

### Continuous Integration (Optional)
- Add a GitHub Actions workflow that runs the unit tests on Windows‑2022 runners on every push.
- Use `dotnet test --configuration Release` and archive test results as an artifact.

### Risk & Mitigation
| Risk | Impact | Mitigation |
|------|--------|------------|
| Incorrect privilege handling causing `STATUS_ACCESS_DENIED` | Service cannot suspend processes from other sessions | Early prototype runs under `LocalSystem` with explicit `SeDebugPrivilege` enable; include fallback validation in client |
| Audio detector COM interop bugs | False‑positive/negative scores | Wrap COM calls in try/catch and fall back to `0` penalty if unavailable |
| Installer signing & UAC prompts | Installation friction for end‑users | Use a code‑signing certificate for the MSI; document the need for admin rights |

---

**Next Steps (immediate)**:
1. Verify that the development machine has the required SDKs (run `dotnet --list-sdks`).
2. Create the solution skeleton (`dotnet new sln -n SnoozeIt`).
3. Scaffold the `SnoozeIt.Core` project (`dotnet new classlib -f net9.0-windows10.0.19041.0`).

I will now create the solution and the first project files.


---

## 11. Live Project Execution Tracker & GitHub Backlog Status

*Synced with GitHub Projects Board: [https://github.com/users/git-maniam/projects/3](https://github.com/users/git-maniam/projects/3)*

| Item / Ticket | Component | Priority | Status | Verification & Deliverables |
|---|---|---|---|---|
| **Phase 1: Core Native Bindings & Process Controller** | `SnoozeIt.Core` | P0 | **Done** | `NtSuspendProcess`, `NtResumeProcess`, `EmptyWorkingSet`, `SetProcessWorkingSetSizeEx`, `SafetyFilter`, `ProcessController`. |
| **Phase 1: Unit Test Suite for SafetyFilter and HeuristicEngine** | `SnoozeIt.Core.Tests` | P0 | **Done** | 21/21 passing automated unit tests verifying clamping, weights, and blacklist checks. |
| **CI/CD: GitHub Actions Pipeline & Code-Signed WiX Installer** | CI / WiX | P0 | **Done** | `.github/workflows/ci.yml` builds `.slnx`, runs tests, packages WiX MSI, and signs with Authenticode cert. |
| **Phase 2: Windows Core Audio (WASAPI) Audio Detector** | `SnoozeIt.Core.Native` | P0 | **Done** | COM interop for `IMMDeviceEnumerator` and `IAudioSessionEnumerator` to flag active audio sessions ($P_{\text{audio}}$). |
| **Phase 2: Live Telemetry & Process Performance Sampler** | `SnoozeIt.Core.Engine` | P1 | **Done** | Background sampler for private working set, CPU usage delta, disk I/O, and network throughput. |
| **Phase 3: SnoozeIt.Service Windows Service & Named Pipe IPC Server** | `SnoozeIt.Service` | P0 | **Done** | Named Pipe server `\\.\pipe\SnoozeIt_IPC` with DACL security and JSON-RPC dispatching. |
| **Phase 3: Multi-User Security & Session Token Access Verification** | `SnoozeIt.Service` | P1 | **Done** | Token impersonation verifying standard user requests target only matching user `SessionId`. |
| **Phase 4: Client IPC Client & Direct Mode Fallback** | `SnoozeIt.Client` | P1 | **Done** | Client pipe connector with graceful fallback to local suspension mode when service is absent. |
| **Phase 4: Instant Thaw on Focus (EVENT_SYSTEM_FOREGROUND Hook)** | `SnoozeIt.Client` | P0 | **Done** | WinEvent hook auto-resuming snoozed processes on window focus ($< 100\text{ ms}$). |
| **Phase 4: WinUI 3 Modern Dashboard UI (Mica Alt & Fluent 2)** | `SnoozeIt.Client` | P0 | **Done** | WinUI 3 desktop dashboard with live metrics banner, process list, and snoozeability circular meters. |
| **Phase 4: System Tray Integration & Background Minimization** | `SnoozeIt.Client` | P1 | **Done** | System tray minimization and context quick actions. |
| **Phase 5: Automation Script & 60-Second Live Demo** | Automation | P1 | **Done** | PowerShell end-to-end demo launching synthetic workloads and showcasing RAM reclamation. |


### 12. Defect & Bug Resolution Log (Synced with GitHub Project)

| Bug ID / Title | Category | Root Cause | Fix / Resolution | Status |
|---|---|---|---|---|
| **[Bug] Service SnoozeIt.Service failed to start during MSI installation** | Packaging / Service | Standard `dotnet build` produces an apphost wrapper that requires `SnoozeIt.Service.dll`. WiX only copied `.exe`, leading to crash on service startup. | Configured `PublishSingleFile=true`, published self-extracting single-file binary, and updated WiX paths. | **Resolved** |
| **[Bug] SnoozeIt.Client.exe crashes on launch: SnoozeIt.Client.dll not found** | Packaging / Client | WiX installer only bundled the standalone `SnoozeIt.Client.exe` apphost stub without the WinUI 3 published DLLs, XAML binaries, and Windows App SDK dependencies. | Integrated WiX `heat` directory harvester in the CI workflow to automatically harvest all published WinUI 3 client dependencies into the MSI. | **Resolved** |
| **[Bug] demo.ps1 invocation fails if run outside repo root directory** | Documentation / Demo | Running `powershell -File .\demo.ps1` from another working directory throws `The argument '.\demo.ps1' does not exist`. | Documented use of absolute path `"D:\Git-Projects\SnoozeIt\demo.ps1"` or navigating to repo root first (`cd D:\Git-Projects\SnoozeIt`). | **Resolved** |
| **[Bug] Signtool Error: Unexpected token 'sign' and Missing Code Signing EKU** | CI/CD | PowerShell interpreted quoted executable as string; self-signed certificate was missing Code Signing EKU (`1.3.6.1.5.5.7.3.3`). | Switched to `shell: cmd` and regenerated PFX using `-Type CodeSigningCert`. | **Resolved** |
| **[Bug] MSBUILD Error MSB1009 and Solution-level RuntimeIdentifier rejection** | Build System | Solution was saved as `.slnx` while workflow looked for `.sln`; passing `-r win-x64` to `.slnx` is disallowed by MSBuild. | Standardized workflow on `SnoozeIt.slnx` and set default RID in `SnoozeIt.Client.csproj`. | **Resolved** |
