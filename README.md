# ARMpulse

A native Windows task manager for your Snapdragon Surface. Inspired by the clarity of [SiliconPulse](https://github.com/kingnazz/SiliconPulse-), independently implemented in C# and WPF for Windows on ARM.

## Run on your Surface

1. Download **ARMpulse-win-arm64.zip** from [Releases](https://github.com/kingnazz/ARMpulse/releases) for a Snapdragon Surface running Windows 11. Extract the ZIP to a folder you control.
2. Open **ARMpulse.exe**. The build is self-contained; you do not need to install .NET, a driver, or a background service.
3. Open **Processes** to search by name, PID, or executable path. Click column headers to sort, or use the **Compatibility**, **High CPU**, and **High memory** filters.

This initial build is unsigned. Windows may show a SmartScreen warning. Only run a build you trust; the ZIP has a companion SHA-256 file for integrity checking. There is no installer or auto-updater. Close the app and delete its folder to remove it; preferences live in `%LOCALAPPDATA%\ARMpulse\settings.json`.

The ZIP is produced in `artifacts/` by the build below. The **Build and validate Windows app** GitHub Actions workflow builds ARM64 and x64 packages and retains them as workflow artifacts. Pushing a `v*` tag runs **Publish portable Windows release**, which tests, builds, and verifies the Windows telemetry and desktop interface before publishing preview downloads with GitHub's built-in token. Surface ARM64 hardware behavior still needs testing. Use version **0.1.1 or later**; the first 0.1.0 preview has a startup binding error.

## What it does

- **Overview:** CPU, physical memory, network receive/send rates, battery and power source, bounded CPU/memory charts, and the busiest processes.
- **Processes:** searchable, numerically sortable, virtualized process table; working sets, CPU, I/O, architecture, executable path, and thread count. Export the filtered, sorted snapshot as CSV.
- **Windows on ARM:** identifies ARM64 and compatibility machine types. `x64 on ARM` can include ARM64EC hybrid apps; it does not mean every module is emulated.
- **Process controls:** open the executable’s folder, set Below Normal CPU priority, or force end an individual process after confirmation. Process actions validate the creation time through the same kernel handle used for the action. Critical, session-zero, inaccessible, stale, and ARMpulse’s own processes are blocked. There is no automatic elevation or process-tree termination.
- **Memory:** physical in-use and available RAM, history, and an explanation of why process working sets do not add up to system memory.
- **History:** the latest 120 samples, with per-sample average and peak statistics. Values and history remain in memory and clear on exit.
- **Preferences:** 1, 2, or 5 second sampling, pause/resume, always on top; minimized sampling slows to at least 5 seconds. One nonoverlapping sampling loop runs off the UI thread.

**Shortcuts:** Ctrl+F searches processes, Ctrl+Space pauses/resumes, F5 refreshes when live. Closing the window exits ARMpulse.

## What it measures

| Metric | Source and meaning |
| --- | --- |
| CPU | `GetSystemTimes` deltas, normalized to total logical CPU capacity. First sample is unavailable. |
| Process CPU | `GetProcessTimes` deltas divided by elapsed time and logical processor count. Total machine capacity is 100%. |
| Physical memory | `GlobalMemoryStatusEx`: total usable physical RAM minus available RAM. Not commit pressure. |
| Process memory | `GetProcessMemoryInfo`: working set, including shared pages. Summing processes can double count pages. |
| Process I/O | `GetProcessIoCounters`: read/write byte deltas. Includes file/device I/O; not disk-only or per-process network throughput. |
| Network | Active network adapter counters, excluding loopback and tunnel adapters. Virtual adapters may double count traffic. First or reset readings are unavailable. |
| Architecture | `IsWow64Process2`. Machine type, not per-module instruction execution. ARM64EC can appear as x64 on ARM. |
| Battery / power | `GetSystemPowerStatus`, as reported by Windows. |

Unavailable values display **—**. Access-restricted processes stay visible. GPU/NPU utilization, temperature, fan speeds, per-core usage, memory compression, startup-app management, services, and local AI model monitoring are **not implemented** in this version. Lower priority is **not** Windows Efficiency Mode, EcoQoS, or a power cap. Sustained CPU/memory thresholds are not thermal or battery diagnostics.

No accounts, telemetry, analytics, network uploads, or external API calls are used by the app. CSV files include executable paths and are written only on request. Process names in CSV are escaped to prevent spreadsheet formula interpretation.

## Build on Windows

Install the **.NET SDK 10.0.402** (pinned in `global.json`), then from this repository:

```powershell
dotnet restore ARMpulse.slnx --locked-mode
dotnet build ARMpulse.slnx -c Release --no-restore
dotnet test tests/ARMpulse.Tests -c Release --no-restore
dotnet run --project src/ARMpulse.Windows
```

To produce the portable ARM64 ZIP:

```powershell
./scripts/Publish.ps1                  # win-arm64, for your Snapdragon Surface
./scripts/Publish.ps1 -Runtime win-x64 # optional Intel/AMD build
```

Use the existing checkout; cloud tasks are already isolated and do not need a Git worktree. Linux can build and cross-publish WPF but cannot run its UI or Windows collectors:

```bash
bash scripts/setup-cloud.sh
source scripts/cloud-env.sh
dotnet publish src/ARMpulse.Windows -c Release -r win-arm64 --self-contained true \
  -p:RestoreLockedMode=true -p:DebugType=None -o artifacts/ARMpulse-win-arm64
```

The cloud setup downloads the pinned Linux SDK from Microsoft and verifies its SHA-512 against official release metadata before extraction. All dependencies come from NuGet with committed lockfiles. Setup needs `builds.dotnet.microsoft.com`, `api.nuget.org`, and NuGet’s artifact endpoints.

## Validation

Core tests cover CPU accounting, elapsed time, counter resets, missing metrics, bounded history, filters, process identities, and CSV safety. GitHub Actions builds both architectures and runs the following on its x64 Windows runner:

```powershell
$app = Start-Process artifacts/ARMpulse-win-x64/ARMpulse.exe `
  -ArgumentList '--smoke-test', "$PWD/artifacts/windows-smoke.json" -Wait -PassThru
if ($app.ExitCode -ne 0) { throw 'Smoke test failed' }
./scripts/Test-Ui.ps1 -Executable artifacts/ARMpulse-win-x64/ARMpulse.exe
```

The native smoke test verifies real telemetry, architecture, and process actions against a disposable child of ARMpulse, including stale identity rejection. The UI test exercises navigation, search, sorting, and pause/resume; rejects WPF binding diagnostics; and captures an actual screenshot on a Windows interactive desktop. Run the equivalent tests with the ARM64 executable on your Surface to validate the native target. The child smoke-test mode automatically exits after 30 seconds if its parent fails.

**Current validation:** cross-compilation and core tests can run in this Linux workspace. Windows telemetry, UI automation, and Surface hardware behavior require Windows; those checks are supplied but have not been executed here. Do not infer a Windows test result from successful cross-compilation.

## Layout

```text
src/ARMpulse.Core       Counter math, process query, bounded history, CSV encoding
src/ARMpulse.Windows    WPF interface, Win32 collectors, process controls, preferences
tests/ARMpulse.Tests   Cross-platform xUnit tests
scripts/               Packaging, Linux setup, Windows UI automation
.github/workflows/     Windows builds and integration checks
```

The source is independently written. No code or image assets from SiliconPulse are bundled. Your machine’s identifiers and specifications are detected at runtime; the screenshot’s serial number, product ID, and device ID are not embedded.
