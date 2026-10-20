ARMpulse's first Windows preview: a native task manager for Windows on ARM.

**For your Snapdragon Surface Laptop, download `ARMpulse-win-arm64.zip`.** Extract it to a folder and run `ARMpulse.exe`. The runtime is included; you do not need to install .NET. `ARMpulse-win-x64.zip` is for Intel/AMD Windows PCs.

Includes live CPU and memory history, network throughput, battery status, searchable and sortable processes, ARM/compatibility architecture visibility, process controls, and CSV export.

This is an unsigned preview intended for testing. Windows may display a SmartScreen warning. GPU/NPU utilization and temperature readings are not implemented. Windows UI and Surface hardware validation are separate from compilation and core tests; please report startup problems or incorrect readings in Issues.

Process termination requires confirmation and can lose unsaved work. ARMpulse does not automatically elevate, end process trees, or install a service.

The `.sha256` files let you verify each ZIP's integrity. See the repository README for metric sources, limitations, and build instructions.
