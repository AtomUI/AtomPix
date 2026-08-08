[CmdletBinding()]
param(
    [ValidateRange(5, 60)]
    [int] $TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows) {
    throw "Windows UI Automation can only run on Windows."
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".artifacts/publish"))
$directory = [System.IO.Path]::GetFullPath((Join-Path $publishRoot "win-x64"))
if (-not $directory.StartsWith($publishRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to execute outside the publish artifact root."
}

$executable = Join-Path $directory "AtomPix.Desktop.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Missing executable: $executable"
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class AtomPixNativeUiInput
{
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
"@

function Find-NamedElement(
    [System.Windows.Automation.AutomationElement] $Root,
    [string] $Name
) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-NamedElement(
    [System.Windows.Automation.AutomationElement] $Root,
    [string] $Name,
    [DateTimeOffset] $Deadline
) {
    while ([DateTimeOffset]::UtcNow -lt $Deadline) {
        $element = Find-NamedElement -Root $Root -Name $Name
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for automation element '$Name'."
}

$process = Start-Process -FilePath $executable -WorkingDirectory $directory -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $handle = [IntPtr]::Zero
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            throw "AtomPix exited before its main window was available with code $($process.ExitCode)."
        }
        $process.Refresh()
        $handle = $process.MainWindowHandle
        if ($handle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 150
    }
    if ($handle -eq [IntPtr]::Zero) { throw "Timed out waiting for the AtomPix main window." }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    if ($null -eq $root) { throw "The AtomPix main window is absent from the UI Automation tree." }
    if ($root.Current.Name -ne "AtomPix") {
        throw "Unexpected main window automation name '$($root.Current.Name)'."
    }

    $navigationNames = @("浏览", "压缩", "转换", "调整尺寸", "裁剪", "批量任务", "设置")
    foreach ($name in $navigationNames) {
        $null = Wait-NamedElement -Root $root -Name $name -Deadline $deadline
    }

    $settings = Find-NamedElement -Root $root -Name "设置"
    $supportedPatternIds = @($settings.GetSupportedPatterns() | ForEach-Object { $_.Id })
    if ($supportedPatternIds -contains [System.Windows.Automation.InvokePattern]::Pattern.Id) {
        $invoke = [System.Windows.Automation.InvokePattern]$settings.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)
        $invoke.Invoke()
    } elseif ($supportedPatternIds -contains [System.Windows.Automation.SelectionItemPattern]::Pattern.Id) {
        $selection = [System.Windows.Automation.SelectionItemPattern]$settings.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
    } else {
        # AtomUI NavMenuNode currently exposes a readable UIA node but no action pattern.
        # Keep UIA as the locator and use a native pointer click only for that framework gap.
        [void][AtomPixNativeUiInput]::ShowWindow($handle, 5)
        [void][AtomPixNativeUiInput]::SetForegroundWindow($handle)
        Start-Sleep -Milliseconds 250
        $rectangle = $settings.Current.BoundingRectangle
        if ($rectangle.IsEmpty -or $rectangle.Width -le 0 -or $rectangle.Height -le 0) {
            throw "The settings navigation element has no clickable UI Automation bounds."
        }
        $x = [int][Math]::Round($rectangle.Left + ($rectangle.Width / 2))
        $y = [int][Math]::Round($rectangle.Top + ($rectangle.Height / 2))
        if (-not [AtomPixNativeUiInput]::SetCursorPos($x, $y)) {
            throw "Could not position the pointer over the settings navigation element."
        }
        [AtomPixNativeUiInput]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        [AtomPixNativeUiInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    }

    $null = Wait-NamedElement -Root $root -Name "保存设置" -Deadline $deadline
    Write-Host "AtomPix real-process UI Automation navigation smoke test passed."
} finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}
