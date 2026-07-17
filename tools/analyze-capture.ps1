[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $Path,

    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

if (-not $Path) {
    $latest = Get-ChildItem -Path (Join-Path $PSScriptRoot '..\captures') -Filter '*.csv' -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latest) { throw 'No capture CSV was found in captures/.' }
    $Path = $latest.FullName
}

$Path = (Resolve-Path -LiteralPath $Path).Path
if (-not $OutputDirectory) { $OutputDirectory = Split-Path -Parent $Path }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function Convert-HexReport([string] $Hex) {
    if (-not $Hex) { return [byte[]]@() }
    if (($Hex.Length % 2) -ne 0) { throw "Invalid report hex: $Hex" }
    $bytes = [byte[]]::new($Hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($Hex.Substring($i * 2, 2), 16)
    }
    return $bytes
}

function Get-CollectionName([string] $Device) {
    if ($Device -match '(?i)&Col([0-9A-F]{2})') { return "Col$($Matches[1].ToUpperInvariant())" }
    return 'Unnumbered'
}

$rows = Import-Csv -LiteralPath $Path
$neutralByDevice = @{}
$attempts = [System.Collections.Generic.List[object]]::new()
$active = $null
$pending = $null

foreach ($row in $rows) {
    switch ($row.kind) {
        'START' {
            $active = [ordered]@{
                Step = [int]$row.step
                Label = $row.label
                StartMs = [long]$row.elapsed_ms
                EndMs = 0L
                Baselines = @{}
                Reports = [System.Collections.Generic.List[object]]::new()
                Status = 'UNMARKED'
            }
            foreach ($key in $neutralByDevice.Keys) {
                $active.Baselines[$key] = [byte[]]$neutralByDevice[$key].Clone()
            }
        }
        'REPORT' {
            $bytes = Convert-HexReport $row.report_hex
            if ($active) {
                $active.Reports.Add([pscustomobject]@{
                    ElapsedMs = [long]$row.elapsed_ms
                    Device = $row.device
                    Collection = Get-CollectionName $row.device
                    Bytes = $bytes
                    Hex = $row.report_hex
                })
            }
            $neutralByDevice[$row.device] = $bytes
        }
        'END' {
            if ($active) {
                $active.EndMs = [long]$row.elapsed_ms
                $pending = $active
                $active = $null
            }
        }
        'ACCEPT' {
            if ($pending) {
                $pending.Status = 'ACCEPT'
                $attempts.Add([pscustomobject]$pending)
                $pending = $null
            }
        }
        'REPEAT' {
            if ($pending) {
                $pending.Status = 'REPEAT'
                $attempts.Add([pscustomobject]$pending)
                $pending = $null
            }
        }
    }
}

# The last accepted attempt for a label wins. This naturally supersedes a step
# that was later reverted and recaptured.
$accepted = $attempts |
    Where-Object Status -eq 'ACCEPT' |
    Group-Object Label |
    ForEach-Object { $_.Group | Select-Object -Last 1 } |
    Sort-Object Step

$analysis = [System.Collections.Generic.List[object]]::new()
foreach ($attempt in $accepted) {
    $deviceGroups = $attempt.Reports | Group-Object Device
    if ($deviceGroups.Count -eq 0) {
        $analysis.Add([pscustomobject]@{
            step = $attempt.Step; control = $attempt.Label; collection = $null
            device = $null; report_length = 0; report_count = 0
            changed_bytes = @(); changed_bits = @(); observed_reports = @()
            note = 'No raw report was captured.'
        })
        continue
    }

    foreach ($group in $deviceGroups) {
        $reports = @($group.Group)
        $baseline = $attempt.Baselines[$group.Name]
        if (-not $baseline -or $baseline.Length -ne $reports[0].Bytes.Length) {
            # Released state is normally the final report in a guided sample.
            $baseline = $reports[-1].Bytes
        }

        $changes = [System.Collections.Generic.List[object]]::new()
        $bitChanges = [System.Collections.Generic.List[string]]::new()
        for ($index = 0; $index -lt $baseline.Length; $index++) {
            $values = @($reports | ForEach-Object { [int]$_.Bytes[$index] })
            $allValues = @([int]$baseline[$index]) + $values
            $minimum = ($allValues | Measure-Object -Minimum).Minimum
            $maximum = ($allValues | Measure-Object -Maximum).Maximum
            $distinct = @($allValues | Sort-Object -Unique)
            if ($distinct.Count -gt 1) {
                $xorMask = 0
                foreach ($value in $values) { $xorMask = $xorMask -bor ($value -bxor $baseline[$index]) }
                $changes.Add([pscustomobject]@{
                    index = $index
                    baseline = [int]$baseline[$index]
                    min = [int]$minimum
                    max = [int]$maximum
                    values = @($distinct)
                    xor_mask_hex = ('0x{0:X2}' -f $xorMask)
                })
                for ($bit = 0; $bit -lt 8; $bit++) {
                    if ($xorMask -band (1 -shl $bit)) { $bitChanges.Add("byte $index bit $bit") }
                }
            }
        }

        $analysis.Add([pscustomobject]@{
            step = $attempt.Step
            control = $attempt.Label
            collection = $reports[0].Collection
            device = $group.Name
            report_length = $reports[0].Bytes.Length
            report_count = $reports.Count
            changed_bytes = @($changes)
            changed_bits = @($bitChanges)
            observed_reports = @($reports.Hex | Sort-Object -Unique)
            note = if ($changes.Count -eq 0) { 'Reports arrived, but no byte differed from the inferred neutral state.' } else { $null }
        })
    }
}

$baseName = [IO.Path]::GetFileNameWithoutExtension($Path)
$jsonPath = Join-Path $OutputDirectory "$baseName.analysis.json"
$markdownPath = Join-Path $OutputDirectory "$baseName.analysis.md"
$analysis | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8

$markdown = [System.Text.StringBuilder]::new()
[void]$markdown.AppendLine('# Fire Controller Capture Analysis')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("Source: ``$([IO.Path]::GetFileName($Path))``")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('Byte indices include the HID report ID at byte 0. Ranges are raw byte values.')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('| Step | Control | Collection | Reports | Changed bytes | Changed bits |')
[void]$markdown.AppendLine('|---:|---|---|---:|---|---|')
foreach ($item in $analysis) {
    $byteText = if ($item.changed_bytes.Count) {
        ($item.changed_bytes | ForEach-Object { "b$($_.index): $($_.baseline) -> $($_.min)..$($_.max) [$($_.xor_mask_hex)]" }) -join '<br>'
    } else { $item.note }
    $bitText = if ($item.changed_bits.Count) { $item.changed_bits -join ', ' } else { '' }
    [void]$markdown.AppendLine("| $($item.step) | $($item.control) | $($item.collection) | $($item.report_count) | $byteText | $bitText |")
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('## Collection report shapes')
[void]$markdown.AppendLine()
foreach ($shape in ($analysis | Where-Object device | Group-Object collection)) {
    $lengths = ($shape.Group.report_length | Sort-Object -Unique) -join ', '
    [void]$markdown.AppendLine("- **$($shape.Name):** report length(s) $lengths bytes")
}
$markdown.ToString() | Set-Content -LiteralPath $markdownPath -Encoding utf8

Write-Output "Analyzed: $Path"
Write-Output "Markdown: $markdownPath"
Write-Output "JSON:     $jsonPath"
