# Isolated Windows shortcut tests. No production Start Menu links are changed.
param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$Namespace,
    [Parameter(Mandatory = $true)][string]$ApplicationName
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ('RBG-shortcut-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$env:RBG_SHORTCUT_TEST_ROOT = $root
$env:RBG_VISUAL_SETTINGS_TEST_ROOT = $root
try {
    Add-Type -AssemblyName System.Windows.Forms
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
    $type = $assembly.GetType("$Namespace.ShortcutSettings", $true)
    $flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
    $save = $type.GetMethod('SaveShortcut', $flags)
    $read = $type.GetMethod('ReadShortcut', $flags)
    $conflict = $type.GetMethod('FindShortcutConflict', $flags)
    $distinct = $type.GetMethod('HasDistinctShortcuts', $flags)
    if ($null -eq $save -or $null -eq $read -or $null -eq $conflict -or $null -eq $distinct) { throw 'Shortcut entry points missing.' }

    $createInput = $type.GetMethod('CreateShortcutInput', $flags)
    $keyDown = [Windows.Forms.Control].GetMethod('OnKeyDown', [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance)
    if ($null -eq $createInput -or $null -eq $keyDown) { throw 'Shortcut capture input entry points missing.' }
    $captureInput = $createInput.Invoke($null, @(0, 0, 200))
    try {
        $keys = [Windows.Forms.Keys]
        $firstKey = [Windows.Forms.KeyEventArgs]::new($keys::Control -bor $keys::Alt -bor $keys::F8)
        $keyDown.Invoke($captureInput, @($firstKey)) | Out-Null
        if ($captureInput.Text -ne 'Ctrl+Alt+F8') { throw "Shortcut capture missed the standard chord: $($captureInput.Text)" }

        $shiftedKey = [Windows.Forms.KeyEventArgs]::new($keys::Control -bor $keys::Alt -bor $keys::Shift -bor $keys::F9)
        $keyDown.Invoke($captureInput, @($shiftedKey)) | Out-Null
        if ($captureInput.Text -ne 'Ctrl+Alt+Shift+F9') { throw "Shortcut capture missed Shift: $($captureInput.Text)" }

        $invalidKey = [Windows.Forms.KeyEventArgs]::new($keys::Control -bor $keys::Alt -bor $keys::D0)
        $keyDown.Invoke($captureInput, @($invalidKey)) | Out-Null
        if ($captureInput.Text -ne 'Ctrl+Alt+Shift+F9') { throw 'Shortcut capture accepted a non-letter/non-function key.' }

        $deleteKey = [Windows.Forms.KeyEventArgs]::new($keys::Delete)
        $keyDown.Invoke($captureInput, @($deleteKey)) | Out-Null
        if ($captureInput.Text -ne '') { throw 'Delete did not clear the captured shortcut.' }
    } finally {
        $captureInput.Dispose()
    }

    if ([bool]$distinct.Invoke($null, @('Ctrl+Alt+F8', 'Alt+Ctrl+F8', 'Ctrl+Alt+P', $true))) {
        throw 'Duplicate shortcuts were accepted when modifier order differed.'
    }
    if ([bool]$distinct.Invoke($null, @('Ctrl+Alt+F8', 'Ctrl+Alt+F9', 'Ctrl+Alt+F8', $true))) {
        throw 'A third-mode duplicate was accepted.'
    }
    if (-not [bool]$distinct.Invoke($null, @('Ctrl+Alt+F8', 'Ctrl+Alt+F9', 'Ctrl+Alt+P', $true))) {
        throw 'Distinct shortcuts were rejected.'
    }

    $modeA = 'Test screen mode'
    $modeB = 'Test window mode'
    $modeC = 'Pause / resume'
    $first = 'Ctrl+Alt+F8'
    $second = 'Ctrl+Alt+F9'
    $third = 'Ctrl+Alt+P'
    $save.Invoke($null, @($ApplicationName, $modeA, '--test-screen', $first)) | Out-Null
    $save.Invoke($null, @($ApplicationName, $modeB, '--test-window', $second)) | Out-Null
    $save.Invoke($null, @($ApplicationName, $modeC, '--toggle-pause', $third)) | Out-Null
    $actualFirst = [string]$read.Invoke($null, @($ApplicationName, $modeA))
    $actualSecond = [string]$read.Invoke($null, @($ApplicationName, $modeB))
    $actualThird = [string]$read.Invoke($null, @($ApplicationName, $modeC))
    $normalize = { param([string]$key) (($key -split '\+' | ForEach-Object { $_.Trim() } | Sort-Object) -join '+').ToUpperInvariant() }
    if ((& $normalize $actualFirst) -ne (& $normalize $first) -or
        (& $normalize $actualSecond) -ne (& $normalize $second) -or
        (& $normalize $actualThird) -ne (& $normalize $third)) {
        throw "Hotkey roundtrip failed: $actualFirst / $actualSecond / $actualThird"
    }

    $links = @(Get-ChildItem -LiteralPath $root -Filter '*.lnk' -Recurse)
    if ($links.Count -ne 3) { throw "Expected three shortcuts, found $($links.Count)." }
    $pauseLink = $links | Where-Object { $_.Name -like '*Pause*resume*.lnk' } | Select-Object -First 1
    if ($null -eq $pauseLink -or $pauseLink.Name.Contains('/') -or $pauseLink.Name.Contains('\')) {
        throw 'Pause/resume mode did not produce a valid shortcut filename.'
    }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut(($links | Where-Object { $_.Name -like "*$modeA*" } | Select-Object -First 1).FullName)
        if ($link.Arguments -ne '--test-screen') { throw 'Shortcut lost its mode argument.' }
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link)
        $link = $shell.CreateShortcut($pauseLink.FullName)
        if ($link.Arguments -ne '--toggle-pause') { throw 'Pause/resume shortcut lost its mode argument.' }
    } finally {
        if ($null -ne $link) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }

    $save.Invoke($null, @('RBG OtherUtility', 'Test other mode', '--other', 'Ctrl+Alt+F10')) | Out-Null
    $found = [string]$conflict.Invoke($null, @($ApplicationName, $modeA, 'Ctrl+Alt+F10', $modeB, $second))
    if (-not $found.Contains('already assigned')) { throw 'Cross-app shortcut conflict was not detected.' }
    $save.Invoke($null, @($ApplicationName, $modeA, '--test-screen', '')) | Out-Null
    if ($null -ne $read.Invoke($null, @($ApplicationName, $modeA))) {
        throw 'Cleared shortcut still exists.'
    }
    $visualType = $assembly.GetType("$Namespace.VisualPreferences", $true)
    $visualFlags = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static
    $settingName = if ($Namespace -eq 'RScreenRec') { 'BadgeSizePercent' } else { 'BorderFadeDurationMs' }
    $settingValue = if ($Namespace -eq 'RScreenRec') { 115 } else { 320 }
    $setting = $visualType.GetProperty($settingName, $visualFlags)
    if ($null -eq $setting) { throw "Visual preference missing: $settingName" }
    $setting.SetValue($null, $settingValue, $null)
    if ([int]$setting.GetValue($null, $null) -ne $settingValue) {
        throw "Visual preference did not roundtrip: $settingName"
    }
    if ($Namespace -eq 'RScreenFlash') {
        $zoomSetting = $visualType.GetProperty('SelectionMagnifierEnabled', $visualFlags)
        $zoomSetting.SetValue($null, $true, $null)
        if (-not [bool]$zoomSetting.GetValue($null, $null)) { throw 'Magnifier preference did not persist.' }
    }
    Write-Host 'Shortcut settings and visual preferences smoke tests passed.'
}
finally {
    Remove-Item Env:\RBG_SHORTCUT_TEST_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:\RBG_VISUAL_SETTINGS_TEST_ROOT -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
