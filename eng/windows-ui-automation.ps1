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
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class AtomPixUiAutomationCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
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

function Assert-No-BrandLogoTakeover([IntPtr] $WindowHandle) {
    $rect = [AtomPixUiAutomationCapture+RECT]::new()
    if (-not [AtomPixUiAutomationCapture]::GetWindowRect($WindowHandle, [ref]$rect)) {
        throw "Unable to read the AtomPix window bounds after opening settings."
    }

    $bitmap = [System.Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $deviceContext = $graphics.GetHdc()
    try {
        if (-not [AtomPixUiAutomationCapture]::PrintWindow($WindowHandle, $deviceContext, 2)) {
            throw "Unable to capture the AtomPix window after opening settings."
        }
    } finally {
        $graphics.ReleaseHdc($deviceContext)
        $graphics.Dispose()
    }

    try {
        $samples = 0
        $brandRedSamples = 0
        for ($y = 0; $y -lt $bitmap.Height; $y += 8) {
            for ($x = 0; $x -lt $bitmap.Width; $x += 8) {
                $color = $bitmap.GetPixel($x, $y)
                $samples++
                if ($color.R -ge 180 -and $color.R -ge ($color.G * 1.35) -and $color.R -ge ($color.B * 1.35)) {
                    $brandRedSamples++
                }
            }
        }

        if ($samples -gt 0 -and ($brandRedSamples / $samples) -gt 0.10) {
            throw "Opening settings caused the AtomPix brand logo to take over the window."
        }
    } finally {
        $bitmap.Dispose()
    }
}

# This gate verifies the published Win32 process and its UI Automation surface.
# The settings action is invoked through UI Automation so the real AtomUI overlay
# composition is covered in addition to deterministic Headless pointer tests.
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

    $navigationNames = @("返回首页", "压缩体积", "转换格式", "调整尺寸", "剪裁尺寸", "设置")
    foreach ($name in $navigationNames) {
        $element = Wait-NamedElement -Root $root -Name $name -Deadline $deadline
        if ($element.Current.ControlType -ne [System.Windows.Automation.ControlType]::Button) {
            throw "Navigation element '$name' is not exposed as a UI Automation button."
        }
        if (-not $element.Current.IsEnabled) {
            throw "Navigation element '$name' is unexpectedly disabled at startup."
        }
    }

    $settingsButton = Find-NamedElement -Root $root -Name "设置"
    $invokePattern = $settingsButton.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    if ($null -eq $invokePattern) {
        throw "The settings navigation button does not expose InvokePattern."
    }
    $invokePattern.Invoke()
    Start-Sleep -Milliseconds 350
    if ($process.HasExited) { throw "AtomPix exited while opening settings." }
    foreach ($name in @("默认配置项", "关于 AtomPix", "保存设置")) {
        $null = Wait-NamedElement -Root $root -Name $name -Deadline $deadline
    }
    Assert-No-BrandLogoTakeover -WindowHandle $handle

    Write-Host "AtomPix published-window, icon-rail, and settings-page UI Automation smoke test passed."
} finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}
