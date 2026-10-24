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
try {
    Add-Type -AssemblyName System.Windows.Forms
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
    $type = $assembly.GetType("$Namespace.ShortcutSettings", $true)
    $flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
    $save = $type.GetMethod('SaveShortcut', $flags)
    $read = $type.GetMethod('ReadShortcut', $flags)
    $conflict = $type.GetMethod('FindShortcutConflict', $flags)
    if ($null -eq $save -or $null -eq $read -or $null -eq $conflict) { throw 'Shortcut entry points missing.' }

    $modeA = 'Test screen mode'
    $modeB = 'Test window mode'
    $first = 'Ctrl+Alt+F8'
    $second = 'Ctrl+Alt+F9'
    $save.Invoke($null, @($ApplicationName, $modeA, '--test-screen', $first)) | Out-Null
    $save.Invoke($null, @($ApplicationName, $modeB, '--test-window', $second)) | Out-Null
    $actualFirst = [string]$read.Invoke($null, @($ApplicationName, $modeA))
    $actualSecond = [string]$read.Invoke($null, @($ApplicationName, $modeB))
    if ($actualFirst -ine $first -or $actualSecond -ine $second) {
        throw "Hotkey roundtrip failed: $actualFirst / $actualSecond"
    }

    $links = @(Get-ChildItem -LiteralPath $root -Filter '*.lnk' -Recurse)
    if ($links.Count -ne 2) { throw "Expected two shortcuts, found $($links.Count)." }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut(($links | Where-Object { $_.Name -like "*$modeA*" } | Select-Object -First 1).FullName)
        if ($link.Arguments -ne '--test-screen') { throw 'Shortcut lost its mode argument.' }
    } finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }

    $save.Invoke($null, @('RBG OtherUtility', 'Test other mode', '--other', 'Ctrl+Alt+F10')) | Out-Null
    $found = [string]$conflict.Invoke($null, @($ApplicationName, $modeA, 'Ctrl+Alt+F10', $modeB, $second))
    if (-not $found.Contains('already assigned')) { throw 'Cross-app shortcut conflict was not detected.' }
    $save.Invoke($null, @($ApplicationName, $modeA, '--test-screen', '')) | Out-Null
    if ($null -ne $read.Invoke($null, @($ApplicationName, $modeA))) {
        throw 'Cleared shortcut still exists.'
    }
    Write-Host 'Shortcut settings smoke tests passed.'
}
finally {
    Remove-Item Env:\RBG_SHORTCUT_TEST_ROOT -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
