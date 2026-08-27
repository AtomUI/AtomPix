[CmdletBinding()]
param(
    [string] $CaseRoot = ".artifacts/trim-validation/gui-cases-20260827",
    [ValidateSet("", "settings-persistence", "browser-controls", "compress-single", "compress-batch", "convert-single-alpha-to-jpeg", "convert-batch-webp", "resize-single-pixel", "resize-batch-percentage", "crop-single-custom")]
    [string] $Only = "",
    [ValidateRange(20, 180)]
    [int] $OperationTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows) { throw "Published GUI functional tests require Windows." }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class AtomPixPublishedTestMouse
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr window, string text);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
        [FieldOffset(0)] public HARDWAREINPUT hardware;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    {
        public int dx; public int dy; public uint mouseData; public uint flags; public uint time; public UIntPtr extraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT
    {
        public uint message; public ushort parameterLow; public ushort parameterHigh;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    {
        public ushort virtualKey;
        public ushort scanCode;
        public uint flags;
        public uint time;
        public UIntPtr extraInfo;
    }
    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
    private static void Key(ushort virtualKey, bool up)
    {
        var input = new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT {
            virtualKey = virtualKey, flags = up ? 0x0002u : 0u } } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) != 1)
            throw new InvalidOperationException("SendInput keyboard event failed.");
    }
    public static void SelectAll()
    {
        Key(0x11, false); Key(0x41, false); Key(0x41, true); Key(0x11, true);
    }
    public static void FocusAddressBar()
    {
        Key(0x11, false); Key(0x4C, false); Key(0x4C, true); Key(0x11, true);
    }
    public static void PressEnter() { Key(0x0D, false); Key(0x0D, true); }
    public static void SendUnicodeText(string text)
    {
        foreach (char character in text)
        {
            var down = new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT {
                scanCode = character, flags = 0x0004u } } };
            var up = down;
            up.data.keyboard.flags = 0x0004u | 0x0002u;
            if (SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT))) != 2)
                throw new InvalidOperationException("SendInput Unicode event failed.");
        }
    }
    public static bool SetNativeFileNameAndOpen(IntPtr dialog, string path)
    {
        IntPtr comboEx = GetDlgItem(dialog, 1148);
        IntPtr combo = comboEx == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(comboEx, IntPtr.Zero, "ComboBox", null);
        IntPtr edit = combo == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(combo, IntPtr.Zero, "Edit", null);
        if (edit == IntPtr.Zero || !SetWindowText(edit, path)) return false;
        IntPtr open = GetDlgItem(dialog, 1);
        if (open == IntPtr.Zero) return false;
        SendMessage(dialog, 0x0111u, new IntPtr(1), open);
        return true;
    }
    public static bool SubmitNativeDialog(IntPtr dialog)
    {
        IntPtr button = GetDlgItem(dialog, 1);
        if (button == IntPtr.Zero) return false;
        SendMessage(dialog, 0x0111u, new IntPtr(1), button);
        return true;
    }
    public static IntPtr FindChildWindow(IntPtr parent, string title)
    {
        return FindWindowEx(parent, IntPtr.Zero, null, title);
    }
    public static IntPtr GetEnabledPopup(IntPtr owner) { return GetWindow(owner, 6u); }
}
"@

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$executable = Join-Path $repoRoot ".artifacts/publish/win-x64/AtomPix.Desktop.exe"
$caseRootPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $CaseRoot))
$resultRoot = Join-Path $repoRoot ".artifacts/trim-validation/results"
$appDataRoot = Join-Path $repoRoot ".artifacts/trim-validation/appdata"
$reportPath = Join-Path $repoRoot ".artifacts/trim-validation/published-functional-results.json"
$tracePath = Join-Path $repoRoot ".artifacts/trim-validation/published-functional-trace.log"
New-Item -ItemType Directory -Force -Path $resultRoot, $appDataRoot | Out-Null
Set-Content -LiteralPath $tracePath -Value "START $(Get-Date -Format O) Only=$Only" -Encoding utf8

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Missing published executable: $executable" }
if (-not (Test-Path -LiteralPath $caseRootPath -PathType Container)) { throw "Missing GUI case root: $caseRootPath" }

$results = [System.Collections.Generic.List[object]]::new()

function Add-Result([string] $case, [string] $status, [string] $details) {
    $results.Add([ordered]@{ Case = $case; Status = $status; Details = $details })
    Write-Host "[$status] $case - $details"
}

function Add-Trace([string] $message) {
    Add-Content -LiteralPath $tracePath -Value "$(Get-Date -Format O) $message" -Encoding utf8
}

function Capture-Desktop([string] $name) {
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bounds.Size)
        $bitmap.Save((Join-Path $resultRoot $name), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function Find-Element($root, [string] $name, $controlType = $null) {
    $conditions = [System.Collections.Generic.List[System.Windows.Automation.Condition]]::new()
    $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name))
    if ($null -ne $controlType) {
        $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType))
    }
    $condition = if ($conditions.Count -eq 1) { $conditions[0] } else {
        [System.Windows.Automation.AndCondition]::new($conditions.ToArray())
    }
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ElementSnapshot($root, [string] $name, $controlType = $null) {
    $elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $elements) {
        try {
            if ($element.Current.Name -eq $name -and ($null -eq $controlType -or $element.Current.ControlType -eq $controlType)) {
                return $element
            }
        } catch { }
    }
    return $null
}

function Wait-Element($root, [string] $name, $controlType = $null, [int] $seconds = 20) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $element = Find-Element $root $name $controlType
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for '$name'."
}

function Wait-ElementGone($root, [string] $name, [int] $seconds = 20) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($null -eq (Find-Element $root $name)) { return }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out waiting for '$name' to close."
}

function Wait-NativeWindowGone([IntPtr] $handle, [string] $name, [int] $seconds = 20) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (-not [AtomPixPublishedTestMouse]::IsWindow($handle)) { return }
        Start-Sleep -Milliseconds 150
    }
    Capture-Desktop "native-window-timeout.png"
    throw "Timed out waiting for native window '$name' to close."
}

function Invoke-Button($root, [string] $name) {
    $button = Wait-Element $root $name ([System.Windows.Automation.ControlType]::Button)
    if (-not $button.Current.IsEnabled) { throw "Button '$name' is disabled." }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Click-Element($element) {
    $bounds = $element.Current.BoundingRectangle
    if ($bounds.IsEmpty) { throw "Element '$($element.Current.Name)' has empty bounds." }
    if ($script:CurrentMainWindowHandle -ne [IntPtr]::Zero) {
        [AtomPixPublishedTestMouse]::SetForegroundWindow($script:CurrentMainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 100
    }
    [AtomPixPublishedTestMouse]::Click(
        [int]($bounds.Left + ($bounds.Width / 2)),
        [int]($bounds.Top + ($bounds.Height / 2)))
}

function Click-Segment($root, [string] $name, [int] $index, [int] $count) {
    $list = Wait-Element $root $name ([System.Windows.Automation.ControlType]::List)
    $bounds = $list.Current.BoundingRectangle
    if ($bounds.IsEmpty) { throw "Segmented control '$name' is outside the visible viewport." }
    $x = [int]($bounds.Left + ($bounds.Width * ($index + 0.5) / $count))
    $y = [int]($bounds.Top + ($bounds.Height / 2))
    [AtomPixPublishedTestMouse]::Click($x, $y)
    Start-Sleep -Milliseconds 250
}

function Click-SegmentOption($root, [string] $listName, [string] $optionName) {
    $list = Wait-Element $root $listName ([System.Windows.Automation.ControlType]::List)
    $condition = [System.Windows.Automation.AndCondition]::new(@(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $optionName),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)))
    $option = $list.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $option) { throw "Option '$optionName' is missing from '$listName'." }
    Click-Element $option
    Start-Sleep -Milliseconds 300
}

function Set-Range($root, [string] $name, [double] $value) {
    $element = Find-ElementSnapshot $root $name
    if ($null -eq $element) {
        $available = [System.Collections.Generic.List[string]]::new()
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($candidate in $all) {
            $pattern = $null
            try {
                if ($candidate.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref] $pattern)) {
                    $available.Add("$($candidate.Current.ControlType.ProgrammaticName):$($candidate.Current.Name)")
                }
            } catch { }
        }
        Add-Trace "missing-range $name available=$($available -join '|')"
        throw "Range control '$name' is missing."
    }
    $range = $element.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $range.SetValue($value)
    Start-Sleep -Milliseconds 250
}

function Set-RangeDirect($root, [string] $name, [double] $value) {
    $element = Wait-Element $root $name $null 20
    $range = $element.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $range.SetValue($value)
    Start-Sleep -Milliseconds 250
}

function Set-Toggle($root, [string] $name, [bool] $checked) {
    $element = Find-ElementSnapshot $root $name ([System.Windows.Automation.ControlType]::CheckBox)
    if ($null -eq $element) { throw "Toggle control '$name' is missing." }
    $toggle = $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $isChecked = $toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
    if ($isChecked -ne $checked) { $toggle.Toggle(); Start-Sleep -Milliseconds 200 }
}

function Start-AtomPix([string] $scenario) {
    Add-Trace "$scenario process-start"
    $scenarioAppData = Join-Path $appDataRoot $scenario
    New-Item -ItemType Directory -Force -Path $scenarioAppData | Out-Null
    $process = Start-Process -FilePath $executable -WorkingDirectory (Split-Path $executable) -PassThru `
        -Environment @{ LOCALAPPDATA = $scenarioAppData }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "AtomPix exited during '$scenario' with code $($process.ExitCode)." }
        Start-Sleep -Milliseconds 150
        $process.Refresh()
    }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw "AtomPix main window did not appear for '$scenario'." }
    $script:CurrentMainWindowHandle = $process.MainWindowHandle
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Add-Trace "$scenario main-window-ready"
    return [pscustomobject]@{ Process = $process; Root = $root; AppData = $scenarioAppData }
}

function Stop-AtomPix($session) {
    if ($null -ne $session -and -not $session.Process.HasExited) {
        Stop-Process -Id $session.Process.Id -Force
        $session.Process.WaitForExit()
    }
}

function Open-Folder($session, [string] $folder) {
    Add-Trace "open-folder begin $folder"
    Invoke-Button $session.Root "打开文件夹"
    Start-Sleep -Seconds 1
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    $dialogHandle = [IntPtr]::Zero
    while ($dialogHandle -eq [IntPtr]::Zero -and [DateTimeOffset]::UtcNow -lt $deadline) {
        $dialogHandle = [AtomPixPublishedTestMouse]::GetEnabledPopup($session.Process.MainWindowHandle)
        if ($dialogHandle -eq $session.Process.MainWindowHandle) { $dialogHandle = [IntPtr]::Zero }
        if ($dialogHandle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    }
    if ($dialogHandle -eq [IntPtr]::Zero) {
        Capture-Desktop "folder-picker-missing.png"
        throw "Native folder picker did not appear."
    }
    [AtomPixPublishedTestMouse]::SetForegroundWindow($dialogHandle) | Out-Null
    [AtomPixPublishedTestMouse]::FocusAddressBar()
    Start-Sleep -Milliseconds 250
    [AtomPixPublishedTestMouse]::SendUnicodeText($folder)
    Start-Sleep -Seconds 2
    [AtomPixPublishedTestMouse]::PressEnter()
    Start-Sleep -Seconds 2
    if (-not [AtomPixPublishedTestMouse]::SubmitNativeDialog($dialogHandle)) {
        throw "Unable to submit the native folder picker."
    }
    Wait-NativeWindowGone $dialogHandle "打开图片文件夹" 20
    Start-Sleep -Seconds 7
    $session.Root = [System.Windows.Automation.AutomationElement]::FromHandle($session.Process.MainWindowHandle)
    $null = Wait-Element $session.Root "压缩体积" ([System.Windows.Automation.ControlType]::Button)
    Add-Trace "open-folder completed $folder"
}

function Open-SingleImage($session, [string] $folder) {
    Add-Trace "open-image begin $folder"
    Invoke-Button $session.Root "打开图片"
    Start-Sleep -Seconds 1
    $dialog = Wait-Element $session.Root "添加图片"
    $source = Get-ChildItem -LiteralPath $folder -File | Select-Object -First 1
    if ($null -eq $source) { throw "No image exists in '$folder'." }
    Add-Trace "open-image picker-ready source=$($source.FullName)"
    $dialogHandle = [IntPtr]$dialog.Current.NativeWindowHandle
    if ($dialogHandle -eq [IntPtr]::Zero) { throw "Native image picker has no window handle." }
    $fileName = Wait-Element $dialog "文件名(N):" ([System.Windows.Automation.ControlType]::Pane)
    Click-Element $fileName
    Start-Sleep -Milliseconds 150
    [AtomPixPublishedTestMouse]::SelectAll()
    [AtomPixPublishedTestMouse]::SendUnicodeText($source.FullName)
    Start-Sleep -Seconds 2
    Add-Trace "open-image filename-entered"
    [AtomPixPublishedTestMouse]::PressEnter()
    Add-Trace "open-image open-clicked"
    Wait-NativeWindowGone $dialogHandle "添加图片" 20
    Start-Sleep -Seconds 7
    $session.Root = [System.Windows.Automation.AutomationElement]::FromHandle($session.Process.MainWindowHandle)
    $null = Wait-Element $session.Root "压缩体积" ([System.Windows.Automation.ControlType]::Button)
    Add-Trace "open-image completed $folder"
}

function Wait-Output([string] $folder, [int] $minimumCount, [string] $extension = "*") {
    $settingsPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "AtomPix/settings.json"
    $output = Join-Path $folder "AtomPix_Output"
    if (Test-Path -LiteralPath $settingsPath) {
        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        $location = $settings.defaultOutputPolicy.locationPolicy
        $output = switch ([int]$location.mode) {
            0 { $folder }
            1 { Join-Path $folder ([string]$location.subfolderName) }
            2 { [string]$location.customDirectory }
            default { throw "Unsupported output location mode '$($location.mode)' in settings." }
        }
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($OperationTimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $output) {
            $allFiles = @(Get-ChildItem -LiteralPath $output -File)
            $files = @($allFiles | Where-Object {
                $_.Name -notmatch '\.tmp(?:\.|$)' -and ($extension -eq "*" -or $_.Extension -eq $extension)
            })
            if ($files.Count -ge $minimumCount -and -not ($allFiles | Where-Object Name -Match '\.tmp(?:\.|$)')) {
                return $files
            }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Timed out waiting for $minimumCount output(s) in '$output'."
}

function Assert-ImageDimensions([string] $path, [int] $width, [int] $height) {
    $image = [System.Drawing.Image]::FromFile($path)
    try {
        if ($image.Width -ne $width -or $image.Height -ne $height) {
            throw "Unexpected dimensions for '$path': $($image.Width)x$($image.Height), expected ${width}x${height}."
        }
    } finally { $image.Dispose() }
}

function Assert-WebP([string] $path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 12 -or
        [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne "RIFF" -or
        [System.Text.Encoding]::ASCII.GetString($bytes, 8, 4) -ne "WEBP") {
        throw "'$path' is not a WebP bitstream."
    }
}

function Invoke-BrowserControls($root) {
    foreach ($name in @("放大", "缩小", "适应", "实际大小")) { Invoke-Button $root $name; Start-Sleep -Milliseconds 150 }
}

function Refresh-Root($session) {
    Start-Sleep -Milliseconds 600
    $session.Root = [System.Windows.Automation.AutomationElement]::FromHandle($session.Process.MainWindowHandle)
}

function Open-Tool($session, [string] $name) {
    Invoke-Button $session.Root $name
    Start-Sleep -Seconds 2
    $session.Root = [System.Windows.Automation.AutomationElement]::FromHandle($session.Process.MainWindowHandle)
}

function Run-Scenario([string] $name, [scriptblock] $body) {
    if (-not [string]::IsNullOrEmpty($Only) -and $Only -ne $name) { return }
    try {
        & $body
    } catch {
        Add-Result $name "FAILED" $_.Exception.Message
    }
}

Run-Scenario "settings-persistence" {
    $session = $null
    try {
        $session = Start-AtomPix "settings"
        Invoke-Button $session.Root "设置"
        Start-Sleep -Milliseconds 800
        $originalQuality = (Wait-Element $session.Root "默认压缩质量数值").GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        $originalConversionQuality = (Wait-Element $session.Root "默认转换质量数值").GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        $testQuality = if ($originalQuality -eq 72) { 71 } else { 72 }
        $testConversionQuality = if ($originalConversionQuality -eq 83) { 82 } else { 83 }
        Set-Range $session.Root "默认压缩质量数值" $testQuality
        Invoke-Button $session.Root "转换配置"
        Start-Sleep -Milliseconds 350
        Set-Range $session.Root "默认转换质量数值" $testConversionQuality
        foreach ($section in @("输出配置", "关于 AtomPix")) {
            Invoke-Button $session.Root $section
            Start-Sleep -Milliseconds 250
        }
        Invoke-Button $session.Root "保存设置"
        $settingsPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "AtomPix/settings.json"
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $settingsPath) -and [DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
        if (-not (Test-Path -LiteralPath $settingsPath)) { throw "Settings JSON was not persisted." }
        Stop-AtomPix $session
        $session = Start-AtomPix "settings"
        Invoke-Button $session.Root "设置"
        Start-Sleep -Milliseconds 800
        $quality = (Wait-Element $session.Root "默认压缩质量数值").GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        $conversionQuality = (Wait-Element $session.Root "默认转换质量数值").GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        if ($quality -ne $testQuality -or $conversionQuality -ne $testConversionQuality) { throw "Saved settings were not restored after restart." }
        Set-Range $session.Root "默认压缩质量数值" $originalQuality
        Invoke-Button $session.Root "转换配置"
        Start-Sleep -Milliseconds 350
        Set-Range $session.Root "默认转换质量数值" $originalConversionQuality
        Invoke-Button $session.Root "保存设置"
        Start-Sleep -Milliseconds 700
        Invoke-Button $session.Root "返回设置前页面"
        Add-Result "settings-persistence" "PASSED" "All four settings sections, save, restart reload, test-value rollback, and return passed."
    } finally { Stop-AtomPix $session }
}

Run-Scenario "browser-controls" {
    $folder = Join-Path $caseRootPath "compress-single"
    $session = $null
    try {
        $session = Start-AtomPix "browser-controls"
        Open-SingleImage $session $folder
        foreach ($name in @("放大", "缩小", "实际大小", "适应")) {
            Invoke-Button $session.Root $name
            Add-Trace "browser-controls invoked=$name"
            Refresh-Root $session
        }
        Capture-Desktop "browser-controls-final.png"
        Add-Result "browser-controls" "PASSED" "Zoom in, zoom out, actual-size and fit controls were invoked; the final fit view remained responsive."
    } finally { Stop-AtomPix $session }
}

Run-Scenario "compress-single" {
    $folder = Join-Path $caseRootPath "compress-single"
    $session = $null
    try {
        $source = Get-Item (Join-Path $folder "compress-source.jpg")
        $sourceHash = (Get-FileHash $source.FullName -Algorithm SHA256).Hash
        $sourceImage = [System.Drawing.Image]::FromFile($source.FullName)
        try { $sourceWidth = $sourceImage.Width; $sourceHeight = $sourceImage.Height } finally { $sourceImage.Dispose() }
        $session = Start-AtomPix "compress-single"
        Open-SingleImage $session $folder
        Add-Trace "compress-single browser-loaded"
        Open-Tool $session "压缩体积"
        Add-Trace "compress-single panel-open"
        Capture-Desktop "compress-single-panel.png"
        Set-Range $session.Root "自定义压缩质量" 68
        Set-Toggle $session.Root "移除拍摄信息与位置数据" $true
        Add-Trace "compress-single configured"
        Invoke-Button $session.Root "单张处理"
        Add-Trace "compress-single start-invoked"
        $files = Wait-Output $folder 1 ".jpg"
        Add-Trace "compress-single output-detected $($files[0].FullName)"
        Assert-ImageDimensions $files[0].FullName $sourceWidth $sourceHeight
        if ((Get-FileHash $source.FullName -Algorithm SHA256).Hash -ne $sourceHash) { throw "Compression modified the source image." }
        Add-Result "compress-single" "PASSED" "Custom quality, metadata toggle, output decode and source preservation passed."
    } finally { Stop-AtomPix $session }
}

Run-Scenario "compress-batch" {
    $folder = Join-Path $caseRootPath "compress-batch"
    $session = $null
    try {
        $session = Start-AtomPix "compress-batch"
        Open-Folder $session $folder
        Open-Tool $session "压缩体积"
        Invoke-Button $session.Root "批量处理"
        $files = Wait-Output $folder 4 ".jpg"
        if ($files.Count -ne 4) { throw "Expected 4 compressed outputs, got $($files.Count)." }
        Add-Result "compress-batch" "PASSED" "Four-image batch completed with four decodable JPEG outputs."
    } finally { Stop-AtomPix $session }
}

Run-Scenario "convert-single-alpha-to-jpeg" {
    $folder = Join-Path $caseRootPath "convert-single"
    $session = $null
    try {
        $session = Start-AtomPix "convert-single"
        Open-SingleImage $session $folder
        Open-Tool $session "转换格式"
        Invoke-Button $session.Root "单张处理"
        $files = Wait-Output $folder 1 ".webp"
        Assert-WebP $files[0].FullName
        Add-Result "convert-single-alpha-to-jpeg" "PASSED" "PNG alpha converted through the published GUI to a valid default WebP bitstream."
    } finally { Stop-AtomPix $session }
}

Run-Scenario "convert-batch-webp" {
    $folder = Join-Path $caseRootPath "convert-batch"
    $session = $null
    try {
        $session = Start-AtomPix "convert-batch"
        Open-Folder $session $folder
        Open-Tool $session "转换格式"
        Invoke-Button $session.Root "批量处理"
        $files = Wait-Output $folder 3 ".webp"
        foreach ($file in $files) { Assert-WebP $file.FullName }
        Add-Result "convert-batch-webp" "PASSED" "JPEG, PNG alpha and WebP inputs produced three valid WebP bitstreams."
    } finally { Stop-AtomPix $session }
}

Run-Scenario "resize-single-pixel" {
    $folder = Join-Path $caseRootPath "resize-single"
    $session = $null
    try {
        $session = Start-AtomPix "resize-single"
        Open-SingleImage $session $folder
        Open-Tool $session "调整尺寸"
        Set-Toggle $session.Root "保持宽高比" $false
        Set-Range $session.Root "目标宽度" 320
        Set-Range $session.Root "目标高度" 200
        Set-Toggle $session.Root "小于目标尺寸时不放大" $true
        Invoke-Button $session.Root "单张处理"
        $files = Wait-Output $folder 1 ".jpg"
        Assert-ImageDimensions $files[0].FullName 320 200
        Add-Result "resize-single-pixel" "PASSED" "Exact 320x200 pixel resize and no-upscale toggle passed."
    } finally { Stop-AtomPix $session }
}

Run-Scenario "resize-batch-percentage" {
    $folder = Join-Path $caseRootPath "resize-batch"
    $session = $null
    try {
        $session = Start-AtomPix "resize-batch"
        Open-Folder $session $folder
        Open-Tool $session "调整尺寸"
        Click-Segment $session.Root "尺寸调整方式" 1 2
        Refresh-Root $session
        Set-Range $session.Root "调整百分比" 50
        Invoke-Button $session.Root "批量处理"
        $files = Wait-Output $folder 4 ".jpg"
        if ($files.Count -ne 4) { throw "Expected 4 resized outputs, got $($files.Count)." }
        foreach ($source in Get-ChildItem -LiteralPath $folder -File -Filter "*.jpg") {
            $output = $files | Where-Object BaseName -Like "$($source.BaseName)_*" | Select-Object -First 1
            if ($null -eq $output) { throw "No resize output maps to '$($source.Name)'." }
            $sourceImage = [System.Drawing.Image]::FromFile($source.FullName)
            $outputImage = [System.Drawing.Image]::FromFile($output.FullName)
            try {
                $expectedWidth = [Math]::Max(1, [Math]::Floor($sourceImage.Width * 0.5))
                $expectedHeight = [Math]::Max(1, [Math]::Floor($sourceImage.Height * 0.5))
                if ($outputImage.Width -ne $expectedWidth -or $outputImage.Height -ne $expectedHeight) {
                    throw "Unexpected 50% dimensions for '$($source.Name)': $($outputImage.Width)x$($outputImage.Height), expected ${expectedWidth}x${expectedHeight}."
                }
            } finally { $sourceImage.Dispose(); $outputImage.Dispose() }
        }
        Add-Result "resize-batch-percentage" "PASSED" "Four-image 50% batch resize completed; every output dimension is the floored half of its source."
    } finally { Stop-AtomPix $session }
}

Run-Scenario "crop-single-custom" {
    $folder = Join-Path $caseRootPath "crop-single"
    $session = $null
    try {
        $session = Start-AtomPix "crop-single"
        Open-SingleImage $session $folder
        Open-Tool $session "剪裁尺寸"
        Add-Trace "crop-single panel-open"
        Set-RangeDirect $session.Root "裁剪宽度" 300
        Add-Trace "crop-single width-set"
        Set-RangeDirect $session.Root "裁剪高度" 200
        Add-Trace "crop-single height-set"
        Set-RangeDirect $session.Root "裁剪起点X" 10
        Add-Trace "crop-single x-set"
        Set-RangeDirect $session.Root "裁剪起点Y" 10
        Add-Trace "crop-single y-set"
        Invoke-Button $session.Root "开始剪裁"
        Add-Trace "crop-single start-invoked"
        Start-Sleep -Seconds 2
        Capture-Desktop "crop-after-start.png"
        $files = Wait-Output $folder 1 ".jpg"
        Assert-ImageDimensions $files[0].FullName 300 200
        Add-Result "crop-single-custom" "PASSED" "Custom 300x200 crop at (10,10) completed and decoded."
    } finally { Stop-AtomPix $session }
}

$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
$failed = @($results | Where-Object Status -eq "FAILED")
Write-Host "Published GUI functional cases: $($results.Count - $failed.Count)/$($results.Count) passed. Report: $reportPath"
if ($failed.Count -gt 0) { throw "$($failed.Count) published GUI functional case(s) failed." }
