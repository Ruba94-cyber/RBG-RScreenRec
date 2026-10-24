[CmdletBinding()]
param(
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'build-check.ps1'
& $buildScript

$runtimeExe = Join-Path $root 'bin\Release\RBG - RScreenRec.exe'
if (-not (Test-Path -LiteralPath $runtimeExe -PathType Leaf)) { throw 'Release executable is missing.' }
$ffmpeg = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
$ffprobe = Get-Command ffprobe.exe -ErrorAction SilentlyContinue
if (-not $ffmpeg -or -not $ffprobe) { throw 'Install FFmpeg and make ffmpeg.exe and ffprobe.exe available on PATH to run media validation.' }

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'The .NET Framework C# compiler was not found.' }

$testRoot = Join-Path $env:TEMP ('RScreenRecSmoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$resolvedTestRoot = (Resolve-Path -LiteralPath $testRoot).Path
$success = $false

try {
    Copy-Item -LiteralPath $runtimeExe -Destination (Join-Path $testRoot 'RBG - RScreenRec.exe')
    $windowHarness = Join-Path $testRoot 'WindowCaptureSmoke.exe'
    $executableWindowHarness = Join-Path $testRoot 'ExecutableWindowCaptureSmoke.exe'
    $longHarness = Join-Path $testRoot 'LongDurationWriterSmoke.exe'
    $targetHarness = Join-Path $testRoot 'SyntheticCaptureTarget.exe'
    & $compiler /nologo /target:winexe "/out:$targetHarness" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $root 'tests\SyntheticCaptureTarget.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the synthetic window target.' }
    $smokeProject = Join-Path $root 'tests\RScreenRec.SmokeAutomation.csproj'
    & dotnet build $smokeProject --configuration Release --output $testRoot --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the FlaUI selected-window smoke harness.' }
    $executableSmokeProject = Join-Path $root 'tests\RScreenRec.ExecutableSmokeAutomation.csproj'
    & dotnet build $executableSmokeProject --configuration Release --output $testRoot --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the FlaUI actual-executable smoke harness.' }
    $settingsSmokeProject = Join-Path $root 'tests\RScreenRec.SettingsUiAutomation.csproj'
    & dotnet build $settingsSmokeProject --configuration Release --output $testRoot --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the FlaUI shortcut-settings smoke harness.' }
    & $compiler /nologo /target:exe "/out:$longHarness" "/reference:$(Join-Path $testRoot 'RBG - RScreenRec.exe')" (Join-Path $root 'tests\LongDurationWriterSmoke.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the long-duration writer harness.' }

    $shortcutTest = Join-Path $root 'tests\Test-Shortcuts.ps1'
    $shortcutTestOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $shortcutTest -Executable $runtimeExe -Namespace RScreenRec -ApplicationName 'RBG RScreenRec' 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Shortcut component tests failed: $($shortcutTestOutput -join ' ')" }
    $settingsHarness = Join-Path $testRoot 'ShortcutSettingsUiSmoke.exe'
    $settingsOutput = & $settingsHarness $runtimeExe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "FlaUI shortcut-settings smoke test failed: $($settingsOutput -join ' ')" }

    $captureCases = @(
        @{ Name = 'odd-landscape'; Width = 671; Height = 377 },
        @{ Name = 'portrait'; Width = 377; Height = 671 },
        @{ Name = 'panorama'; Width = 1001; Height = 311 }
    )
    $results = @()
    foreach ($case in $captureCases) {
        $video = Join-Path $testRoot ($case.Name + '.mp4')
        $harnessOutput = & $windowHarness $video $case.Width $case.Height 7 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Window capture failed for $($case.Name): $($harnessOutput -join ' ')" }
        if (-not ($harnessOutput -join ' ').Contains('backend=Windows Graphics Capture')) {
            throw "Windows Graphics Capture was not selected for $($case.Name): $($harnessOutput -join ' ')"
        }

        $probeJson = & $ffprobe.Source -v error -select_streams v:0 -show_entries 'stream=codec_name,width,height,avg_frame_rate,nb_frames,duration:format=size' -of json $video
        if ($LASTEXITCODE -ne 0) { throw "ffprobe failed for $($case.Name)." }
        $probe = ($probeJson -join [Environment]::NewLine) | ConvertFrom-Json
        $stream = $probe.streams | Select-Object -First 1
        $expectedWidth = [int]([Math]::Ceiling($case.Width / 2.0) * 2)
        $expectedHeight = [int]([Math]::Ceiling($case.Height / 2.0) * 2)
        if ($stream.codec_name -ne 'h264' -or $stream.width -ne $expectedWidth -or $stream.height -ne $expectedHeight) {
            throw "Unexpected encoded format for $($case.Name): $($stream | ConvertTo-Json -Compress)"
        }
        if ($stream.avg_frame_rate -ne '30/1' -or [int64]$stream.nb_frames -lt 180) {
            throw "Unexpected frame cadence or count for $($case.Name): $($stream | ConvertTo-Json -Compress)"
        }

        & $ffmpeg.Source -v error -xerror -i $video -f null NUL
        if ($LASTEXITCODE -ne 0) { throw "Full MP4 decode failed for $($case.Name)." }

        $occludedFrame = Join-Path $testRoot ($case.Name + '-occluded.png')
        $restoredFrame = Join-Path $testRoot ($case.Name + '-restored.png')
        & $ffmpeg.Source -v error -ss 1.2 -i $video -frames:v 1 -update 1 -y $occludedFrame
        if ($LASTEXITCODE -ne 0) { throw "Could not extract the occlusion-check frame for $($case.Name)." }
        & $ffmpeg.Source -v error -ss 5.0 -i $video -frames:v 1 -update 1 -y $restoredFrame
        if ($LASTEXITCODE -ne 0) { throw "Could not extract the restore-check frame for $($case.Name)." }

        Add-Type -AssemblyName System.Drawing
        $before = [System.Drawing.Bitmap]::FromFile($occludedFrame)
        $after = [System.Drawing.Bitmap]::FromFile($restoredFrame)
        try {
            $a = $before.GetPixel([int]($before.Width / 2), [int]($before.Height / 2))
            $b = $after.GetPixel([int]($after.Width / 2), [int]($after.Height / 2))
            if ($a.R -gt 180 -and $a.G -lt 80 -and $a.B -gt 180) {
                throw "The unrelated magenta occluder appeared in the $($case.Name) recording."
            }
            $pixelChange = [Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B)
            if ($pixelChange -lt 12) { throw "The $($case.Name) frame did not change after target restore." }
        }
        finally {
            $before.Dispose()
            $after.Dispose()
        }

        $results += [pscustomobject]@{
            Test = $case.Name
            Backend = 'Windows Graphics Capture'
            SizeBytes = [int64]$probe.format.size
            Encoded = "$($stream.width)x$($stream.height)"
            FPS = $stream.avg_frame_rate
            Frames = [int64]$stream.nb_frames
            DurationSeconds = [double]$stream.duration
            FullDecode = 'ok'
            OcclusionAndRestore = 'ok'
        }
    }

    $pausedVideo = Join-Path $testRoot 'paused-window.mp4'
    $pausedOutput = & $windowHarness $pausedVideo 671 377 5 pause 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Pause/resume test failed: $($pausedOutput -join ' ')" }
    $pausedProbeJson = & $ffprobe.Source -v error -show_entries 'format=duration' -of json $pausedVideo
    if ($LASTEXITCODE -ne 0) { throw 'ffprobe could not inspect the paused MP4.' }
    $pausedProbe = ($pausedProbeJson -join [Environment]::NewLine) | ConvertFrom-Json
    $effectiveDuration = [double]::Parse([string]$pausedProbe.format.duration, [Globalization.CultureInfo]::InvariantCulture)
    if ($effectiveDuration -lt 3.4 -or $effectiveDuration -gt 4.8) {
        throw "Paused timeline has an unexpected duration: $effectiveDuration seconds; expected about 4 seconds."
    }
    & $ffmpeg.Source -v error -xerror -i $pausedVideo -f null NUL
    if ($LASTEXITCODE -ne 0) { throw 'Paused/resumed MP4 could not fully decode.' }
    $results += [pscustomobject]@{
        Test = 'pause-resume'
        Backend = 'Windows Graphics Capture'
        SizeBytes = [int64](Get-Item -LiteralPath $pausedVideo).Length
        Encoded = '671x377 padded'
        FPS = '30/1'
        Frames = 'validated via duration'
        DurationSeconds = $effectiveDuration
        FullDecode = 'ok'
        OcclusionAndRestore = 'not-applicable'
    }

    $closedVideo = Join-Path $testRoot 'closed-window.mp4'
    $closeOutput = & $windowHarness $closedVideo 671 377 2 close 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Closing the selected window failed: $($closeOutput -join ' ')" }
    if (-not ($closeOutput -join ' ').Contains('source_close=expected; output_finalized=true')) {
        throw "Window close was not reported and finalized: $($closeOutput -join ' ')"
    }
    $closedJson = & $ffprobe.Source -v error -select_streams v:0 -show_entries 'stream=codec_name,width,height,avg_frame_rate,nb_frames,duration:format=size' -of json $closedVideo
    if ($LASTEXITCODE -ne 0) { throw 'ffprobe failed for the closed-window MP4.' }
    $closedProbe = ($closedJson -join [Environment]::NewLine) | ConvertFrom-Json
    $closedStream = $closedProbe.streams | Select-Object -First 1
    if ($closedStream.codec_name -ne 'h264' -or $closedStream.avg_frame_rate -ne '30/1' -or [int64]$closedStream.nb_frames -lt 30) {
        throw "Unexpected closed-window stream metadata: $($closedStream | ConvertTo-Json -Compress)"
    }
    & $ffmpeg.Source -v error -xerror -i $closedVideo -f null NUL
    if ($LASTEXITCODE -ne 0) { throw 'The closed-window MP4 did not fully decode.' }
    $results += [pscustomobject]@{
        Test = 'closed-window'
        Backend = 'Windows Graphics Capture'
        SizeBytes = [int64]$closedProbe.format.size
        Encoded = "$($closedStream.width)x$($closedStream.height)"
        FPS = $closedStream.avg_frame_rate
        Frames = [int64]$closedStream.nb_frames
        DurationSeconds = [double]$closedStream.duration
        FullDecode = 'ok'
        OcclusionAndRestore = 'not-applicable'
    }

    $executableOutput = Join-Path $testRoot 'actual-executable-output'
    New-Item -ItemType Directory -Path $executableOutput | Out-Null
    $executableOutputText = & $executableWindowHarness (Join-Path $testRoot 'RBG - RScreenRec.exe') $executableOutput 671 377 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Actual RScreenRec executable Shift/window capture failed: $($executableOutputText -join ' ')" }
    if (-not ($executableOutputText -join ' ').Contains('shift_picker=seen_with_shift_held') -or
        -not ($executableOutputText -join ' ').Contains('occlusion_and_resume=ok') -or
        -not ($executableOutputText -join ' ').Contains('recording_overlay=visible') -or
        -not ($executableOutputText -join ' ').Contains('pause_resume=ok') -or
        -not ($executableOutputText -join ' ').Contains('full_screen_overlay=visible') -or
        -not ($executableOutputText -join ' ').Contains('overlay_drag=persisted') -or
        -not ($executableOutputText -join ' ').Contains('overlay_position_reloaded=true') -or
        -not ($executableOutputText -join ' ').Contains('full_screen_recording=finalized')) {
        throw "Actual-executable Shift/full-screen overlay/pause evidence was incomplete: $($executableOutputText -join ' ')"
    }
    $executableVideo = Get-ChildItem -LiteralPath $executableOutput -Filter 'rec_*.mp4' -File | Select-Object -First 1
    if (-not $executableVideo) { throw 'The actual executable did not produce an MP4 in the isolated smoke folder.' }
    $executableJson = & $ffprobe.Source -v error -select_streams v:0 -show_entries 'stream=codec_name,width,height,avg_frame_rate,nb_frames,duration:format=size' -of json $executableVideo.FullName
    if ($LASTEXITCODE -ne 0) { throw 'ffprobe failed for the actual-executable window recording.' }
    $executableProbe = ($executableJson -join [Environment]::NewLine) | ConvertFrom-Json
    $executableStream = $executableProbe.streams | Select-Object -First 1
    if ($executableStream.codec_name -ne 'h264' -or $executableStream.width -ne 672 -or $executableStream.height -ne 378 -or
        $executableStream.avg_frame_rate -ne '30/1' -or [int64]$executableStream.nb_frames -lt 150 -or [double]$executableStream.duration -lt 6) {
        throw "Unexpected actual-executable stream metadata: $($executableStream | ConvertTo-Json -Compress)"
    }
    if ([double]$executableStream.duration -lt 7.2 -or [double]$executableStream.duration -gt 8.8) {
        throw "Actual-executable pause timeline did not exclude the pause interval: $($executableStream.duration) seconds; expected about 8 seconds."
    }
    & $ffmpeg.Source -v error -xerror -i $executableVideo.FullName -f null NUL
    if ($LASTEXITCODE -ne 0) { throw 'The actual-executable window recording did not fully decode.' }

    $actualOccludedFrame = Join-Path $testRoot 'actual-executable-occluded.png'
    $actualBaselineFrame = Join-Path $testRoot 'actual-executable-baseline.png'
    $actualRestoredFrame = Join-Path $testRoot 'actual-executable-restored.png'
    & $ffmpeg.Source -v error -ss 1.5 -i $executableVideo.FullName -frames:v 1 -update 1 -y $actualOccludedFrame
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the actual-executable occlusion frame.' }
    & $ffmpeg.Source -v error -ss 0.5 -i $executableVideo.FullName -frames:v 1 -update 1 -y $actualBaselineFrame
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the actual-executable baseline frame.' }
    & $ffmpeg.Source -v error -ss 5.0 -i $executableVideo.FullName -frames:v 1 -update 1 -y $actualRestoredFrame
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the actual-executable restored frame.' }
    $occludedBitmap = [System.Drawing.Bitmap]::FromFile($actualOccludedFrame)
    $baselineBitmap = [System.Drawing.Bitmap]::FromFile($actualBaselineFrame)
    $restoredBitmap = [System.Drawing.Bitmap]::FromFile($actualRestoredFrame)
    try {
        $magentaPixels = 0
        for ($y = 0; $y -lt $occludedBitmap.Height; $y += 2) {
            for ($x = 0; $x -lt $occludedBitmap.Width; $x += 2) {
                $pixel = $occludedBitmap.GetPixel($x, $y)
                if ($pixel.R -gt 240 -and $pixel.G -lt 15 -and $pixel.B -gt 240) { $magentaPixels++ }
            }
        }
        $sampledPixels = [Math]::Ceiling($occludedBitmap.Width / 2.0) * [Math]::Ceiling($occludedBitmap.Height / 2.0)
        if ($magentaPixels -gt ($sampledPixels * 0.01)) {
            throw "The covering magenta window appeared in the actual-executable recording ($magentaPixels/$sampledPixels sampled pixels)."
        }
        $before = $baselineBitmap.GetPixel([int]($baselineBitmap.Width / 2), [int]($baselineBitmap.Height / 2))
        $after = $restoredBitmap.GetPixel([int]($restoredBitmap.Width / 2), [int]($restoredBitmap.Height / 2))
        $pixelChange = [Math]::Abs($before.R - $after.R) + [Math]::Abs($before.G - $after.G) + [Math]::Abs($before.B - $after.B)
        if ($pixelChange -lt 24) { throw 'The selected window did not resume changing after occlusion and restore in the actual-executable recording.' }
    }
    finally {
        $occludedBitmap.Dispose()
        $baselineBitmap.Dispose()
        $restoredBitmap.Dispose()
    }
    $results += [pscustomobject]@{
        Test = 'actual-executable-shift-window-occlusion'
        Backend = 'Windows Graphics Capture (no compatibility warning)'
        SizeBytes = [int64]$executableProbe.format.size
        Encoded = "$($executableStream.width)x$($executableStream.height)"
        FPS = $executableStream.avg_frame_rate
        Frames = [int64]$executableStream.nb_frames
        DurationSeconds = [double]$executableStream.duration
        FullDecode = 'ok'
        OcclusionAndRestore = 'ok; no magenta cover in stream'
    }

    $fullScreenOverlayDirectory = Join-Path $executableOutput 'full-screen-overlay'
    $fullScreenVideo = Get-ChildItem -LiteralPath $fullScreenOverlayDirectory -Filter 'rec_*.mp4' -File |
        Sort-Object LastWriteTime | Select-Object -First 1
    if (-not $fullScreenVideo) { throw 'The full-screen overlay smoke did not produce a finalized MP4.' }
    $fullScreenJson = & $ffprobe.Source -v error -select_streams v:0 -show_entries 'stream=codec_name,width,height,avg_frame_rate,duration:format=size' -of json $fullScreenVideo.FullName
    if ($LASTEXITCODE -ne 0) { throw 'ffprobe failed for the synthetic full-screen recording.' }
    $fullScreenProbe = ($fullScreenJson -join [Environment]::NewLine) | ConvertFrom-Json
    $fullScreenStream = $fullScreenProbe.streams | Select-Object -First 1
    if ($fullScreenStream.codec_name -ne 'h264' -or $fullScreenStream.avg_frame_rate -ne '30/1' -or
        [double]$fullScreenStream.duration -lt 1.5) {
        throw "Unexpected synthetic full-screen stream metadata: $($fullScreenStream | ConvertTo-Json -Compress)"
    }
    & $ffmpeg.Source -v error -xerror -i $fullScreenVideo.FullName -f null NUL
    if ($LASTEXITCODE -ne 0) { throw 'The synthetic full-screen MP4 did not fully decode.' }
    $overlayProbeValues = (Get-Content -LiteralPath (Join-Path $fullScreenOverlayDirectory 'overlay-capture-probe.txt') -Raw).Split(',')
    if ($overlayProbeValues.Length -ne 7) { throw 'The REC overlay capture probe is malformed.' }
    $screenLeft = [int]$overlayProbeValues[0]
    $screenTop = [int]$overlayProbeValues[1]
    $overlayLeft = [int]$overlayProbeValues[2]
    $overlayTop = [int]$overlayProbeValues[3]
    $overlayWidth = [int]$overlayProbeValues[4]
    $overlayHeight = [int]$overlayProbeValues[5]
    $affinityEnabled = $overlayProbeValues[6] -eq '1'
    if ($affinityEnabled) {
        $overlayFrame = Join-Path $testRoot 'full-screen-overlay-frame.png'
        & $ffmpeg.Source -v error -ss 1.5 -i $fullScreenVideo.FullName -frames:v 1 -update 1 -y $overlayFrame
        if ($LASTEXITCODE -ne 0) { throw 'Could not extract the full-screen REC exclusion frame.' }
        $overlayBitmap = [System.Drawing.Bitmap]::FromFile($overlayFrame)
        try {
            $sampleX = $overlayLeft - $screenLeft + [int]($overlayWidth / 2)
            $sampleY = $overlayTop - $screenTop + [int]($overlayHeight / 2)
            if ($sampleX -lt 0 -or $sampleX -ge $overlayBitmap.Width -or $sampleY -lt 0 -or $sampleY -ge $overlayBitmap.Height) {
                throw 'The saved REC overlay position lies outside the encoded full-screen frame.'
            }
            $overlayPixel = $overlayBitmap.GetPixel($sampleX, $sampleY)
            if ($overlayPixel.R -gt 40 -or $overlayPixel.G -lt 210 -or $overlayPixel.B -lt 210) {
                throw "The REC overlay appeared in the full-screen recording although capture exclusion was enabled (pixel $($overlayPixel.R),$($overlayPixel.G),$($overlayPixel.B))."
            }
        }
        finally {
            $overlayBitmap.Dispose()
        }
    }
    $results += [pscustomobject]@{
        Test = 'actual-executable-full-screen-rec-overlay'
        Backend = 'Desktop capture with synthetic cyan surface'
        SizeBytes = [int64]$fullScreenProbe.format.size
        Encoded = "$($fullScreenStream.width)x$($fullScreenStream.height)"
        FPS = $fullScreenStream.avg_frame_rate
        Frames = 'validated via duration and full decode'
        DurationSeconds = [double]$fullScreenStream.duration
        FullDecode = 'ok'
        OcclusionAndRestore = if ($affinityEnabled) { 'REC excluded; drag and restart position persisted' } else { 'exclusion unavailable; drag and restart position persisted' }
    }

    $longVideo = Join-Path $testRoot 'over-one-hour.mp4'
    $longOutput = & $longHarness $longVideo 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Long-duration writer failed: $($longOutput -join ' ')" }
    $longJson = & $ffprobe.Source -v error -select_streams v:0 -show_entries 'stream=codec_name,width,height,avg_frame_rate,nb_frames,duration:format=size' -of json $longVideo
    if ($LASTEXITCODE -ne 0) { throw 'ffprobe failed for the long-duration MP4.' }
    $longProbe = ($longJson -join [Environment]::NewLine) | ConvertFrom-Json
    $longStream = $longProbe.streams | Select-Object -First 1
    if ($longStream.avg_frame_rate -ne '30/1' -or [int64]$longStream.nb_frames -ne 108030 -or [double]$longStream.duration -lt 3601) {
        throw "Unexpected long-duration stream metadata: $($longStream | ConvertTo-Json -Compress)"
    }
    & $ffmpeg.Source -v error -xerror -i $longVideo -f null NUL
    if ($LASTEXITCODE -ne 0) { throw 'Full decode failed for the long-duration MP4.' }
    $results | Format-Table -AutoSize
    [pscustomobject]@{
        Test = 'writer-over-one-hour'
        SizeBytes = [int64]$longProbe.format.size
        Encoded = "$($longStream.width)x$($longStream.height)"
        FPS = $longStream.avg_frame_rate
        Frames = [int64]$longStream.nb_frames
        DurationSeconds = [double]$longStream.duration
        FullDecode = 'ok'
    } | Format-List
    $success = $true
}
finally {
    if ($success -and -not $KeepArtifacts) {
        $tempPrefix = [System.IO.Path]::GetFullPath((Join-Path $env:TEMP 'RScreenRecSmoke-'))
        if (-not $resolvedTestRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a test path outside the dedicated temporary directory: $resolvedTestRoot"
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $resolvedTestRoot) {
        Write-Host "Smoke-test artifacts retained at: $resolvedTestRoot"
    }
}
