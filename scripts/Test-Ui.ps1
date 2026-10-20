param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$Output = 'artifacts/ui-smoke'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
New-Item -ItemType Directory -Force $Output | Out-Null
$outputPath = (Resolve-Path $Output).Path
$bindingLog = Join-Path $outputPath 'bindings.log'
Remove-Item $bindingLog -ErrorAction SilentlyContinue
$app = Start-Process (Resolve-Path $Executable) -ArgumentList @('--ui-test', "`"$bindingLog`"") -PassThru
$root = $null
$checks = [System.Collections.Generic.List[string]]::new()

function Find-Element([string]$Name, [System.Windows.Automation.ControlType]$Type) {
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Name),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $Type))
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($app.HasExited) { throw "App exited unexpectedly: $($app.ExitCode)" }
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($element -and !$element.Current.IsOffscreen) { return $element }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Visible UI element not found: $Name"
}

function Select-Page([string]$Name) {
    $item = Find-Element $Name ([System.Windows.Automation.ControlType]::ListItem)
    ([System.Windows.Automation.SelectionItemPattern]$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
}

function Invoke-Button([string]$Name) {
    $button = Find-Element $Name ([System.Windows.Automation.ControlType]::Button)
    ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        if ($app.HasExited) { throw "App failed to start: $($app.ExitCode)" }
        $app.Refresh()
        if ($app.MainWindowHandle -ne 0) {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
            break
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$root) { throw 'No application window.' }
    Find-Element 'Your Surface, at a glance.' ([System.Windows.Automation.ControlType]::Text) | Out-Null
    $checks.Add('Window starts and overview renders')

    Select-Page 'Processes'
    $search = Find-Element 'Search processes' ([System.Windows.Automation.ControlType]::Edit)
    $value = [System.Windows.Automation.ValuePattern]$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue('ARMpulse-no-such-process-029a')
    Find-Element 'No processes match this search.' ([System.Windows.Automation.ControlType]::Text) | Out-Null
    $value.SetValue('')
    $checks.Add('Process search updates the visible results')

    foreach ($header in @('Memory', 'CPU')) {
        $column = Find-Element $header ([System.Windows.Automation.ControlType]::HeaderItem)
        ([System.Windows.Automation.InvokePattern]$column.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    }
    $checks.Add('Numeric process column sorting is interactive')
    Invoke-Button 'Pause'
    Find-Element 'PAUSED' ([System.Windows.Automation.ControlType]::Text) | Out-Null
    Invoke-Button 'Resume'
    $checks.Add('Pause and resume work')

    Select-Page 'Memory'
    Find-Element 'PHYSICAL MEMORY IN USE' ([System.Windows.Automation.ControlType]::Text) | Out-Null
    Select-Page 'History'
    Find-Element 'Session history' ([System.Windows.Automation.ControlType]::Text) | Out-Null
    Select-Page 'Settings'
    Find-Element 'Refresh interval in seconds' ([System.Windows.Automation.ControlType]::ComboBox) | Out-Null
    $checks.Add('Memory, history, and settings pages render')
    Select-Page 'Overview'
    Start-Sleep -Seconds 3
    $bounds = $root.Current.BoundingRectangle
    $bitmap = [System.Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.Left, [int]$bounds.Top, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $outputPath 'overview.png'))
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    $app.CloseMainWindow() | Out-Null
    if (!$app.WaitForExit(10000)) { throw 'App did not quit after its window closed.' }
    if ($app.ExitCode -ne 0) { throw "App exited with $($app.ExitCode)." }
    if ((Test-Path $bindingLog) -and (Get-Item $bindingLog).Length -gt 0) {
        Get-Content $bindingLog
        throw 'WPF reported binding warnings or errors.'
    }
    @{ passed = $true; checks = $checks.ToArray() } | ConvertTo-Json | Set-Content (Join-Path $outputPath 'result.json')
} catch {
    $detail = $_.Exception.Message + ' | Completed checks: ' + ($checks -join '; ')
    if (!$app.HasExited) { $app.CloseMainWindow() | Out-Null; $app.WaitForExit(3000) | Out-Null }
    if (Test-Path $bindingLog) { $detail += ' | WPF diagnostics: ' + (Get-Content $bindingLog -Raw) }
    $escaped = $detail.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Output "::error title=Desktop smoke test::$escaped"
    throw
} finally {
    if (!$app.HasExited) { $app.Kill(); $app.WaitForExit() }
}
