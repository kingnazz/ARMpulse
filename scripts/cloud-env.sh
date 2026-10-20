#!/usr/bin/env bash
# Source this file in the Linux cloud environment before using dotnet.
export DOTNET_ROOT=/workspace/tooling/dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/tooling/dotnet-home
export NUGET_PACKAGES=/workspace/tooling/nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
