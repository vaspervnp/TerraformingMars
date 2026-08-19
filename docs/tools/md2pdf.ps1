<#
.SYNOPSIS
    Renders the GAMEPLAY guides to the PDFs under docs/.

.DESCRIPTION
    Small Markdown -> HTML converter (headings, rules, nested lists with ordered-list start
    numbers, blockquotes, inline bold/italic/code/links) printed through headless Edge or Chrome.
    That combination is used on purpose: Greek text comes out in Segoe UI and the section emoji
    in full colour via Segoe UI Emoji - most Markdown->PDF engines drop one or the other.

    The language-switcher / download line is stripped, so the PDF does not link to itself;
    everything else matches the Markdown one-to-one.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File docs/tools/md2pdf.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,                                 # default: two levels above this script
    [string]$Browser,                                  # optional path to msedge.exe / chrome.exe
    [switch]$KeepHtml                                  # leave the intermediate HTML next to the PDF
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = (Get-Item $PSCommandPath).Directory.Parent.Parent.FullName }

$documents = @(
    @{ Source = 'GAMEPLAY.md';    Pdf = 'docs/GAMEPLAY.en.pdf' },
    @{ Source = 'GAMEPLAY.el.md'; Pdf = 'docs/GAMEPLAY.el.pdf' }
)

# ----------------------------------------------------------------- Markdown -> HTML

function Convert-Inline([string]$text) {
    $t = $text -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'
    $t = [regex]::Replace($t, '`([^`]+)`', '<code>$1</code>')
    $t = [regex]::Replace($t, '\[([^\]]+)\]\(([^)]+)\)', '<a href="$2">$1</a>')
    $t = [regex]::Replace($t, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
    $t = [regex]::Replace($t, '(?<![\*\w])\*([^*]+)\*(?!\*)', '<em>$1</em>')
    return $t
}

function Convert-Markdown([string[]]$lines) {
    $out = New-Object System.Text.StringBuilder
    $listStack = New-Object System.Collections.Generic.List[object]   # @{ Tag; Indent }
    $para = New-Object System.Collections.Generic.List[string]
    $inQuote = $false

    function Close-Lists([int]$toIndent) {
        while ($listStack.Count -gt 0 -and $listStack[$listStack.Count - 1].Indent -ge $toIndent) {
            [void]$out.AppendLine("</$($listStack[$listStack.Count - 1].Tag)>")
            $listStack.RemoveAt($listStack.Count - 1)
        }
    }

    # Soft-wrapped Markdown: consecutive plain lines are ONE paragraph, not one each.
    function Close-Para {
        if ($para.Count -gt 0) {
            [void]$out.AppendLine("<p>$(Convert-Inline ($para -join ' '))</p>")
            $para.Clear()
        }
    }

    foreach ($raw in $lines) {
        $line = $raw.TrimEnd()

        # The language switcher / "download the PDF" line only makes sense on the web.
        if ($line -match '\]\((docs/)?GAMEPLAY\.(en|el)\.(pdf|md)\)') { continue }

        $quoted = $false
        if ($line -match '^\s*>\s?(.*)$') { $quoted = $true; $line = $Matches[1] }

        if ($quoted -ne $inQuote) {
            Close-Para
            Close-Lists 0
            [void]$out.AppendLine($(if ($quoted) { '<blockquote>' } else { '</blockquote>' }))
            $inQuote = $quoted
        }

        if ([string]::IsNullOrWhiteSpace($line)) { Close-Para; Close-Lists 0; continue }

        if ($line -match '^(#{1,6})\s+(.*)$') {
            Close-Para; Close-Lists 0
            $level = $Matches[1].Length
            [void]$out.AppendLine("<h$level>$(Convert-Inline $Matches[2])</h$level>")
            continue
        }

        if ($line -match '^\s*(---+|___+|\*\*\*+)\s*$') {
            Close-Para; Close-Lists 0
            [void]$out.AppendLine('<hr />')
            continue
        }

        if ($line -match '^(\s*)[-*]\s+(.*)$') {
            Close-Para
            $indent = $Matches[1].Length
            $text = $Matches[2]
            Close-Lists ($indent + 1)
            if ($listStack.Count -eq 0 -or $listStack[$listStack.Count - 1].Indent -lt $indent) {
                [void]$out.AppendLine('<ul>')
                $listStack.Add(@{ Tag = 'ul'; Indent = $indent })
            }
            [void]$out.AppendLine("<li>$(Convert-Inline $text)</li>")
            continue
        }

        if ($line -match '^(\s*)(\d+)\.\s+(.*)$') {
            Close-Para
            $indent = $Matches[1].Length
            $start = $Matches[2]
            $text = $Matches[3]
            Close-Lists ($indent + 1)
            if ($listStack.Count -eq 0 -or $listStack[$listStack.Count - 1].Indent -lt $indent) {
                [void]$out.AppendLine("<ol start=""$start"">")
                $listStack.Add(@{ Tag = 'ol'; Indent = $indent })
            }
            [void]$out.AppendLine("<li>$(Convert-Inline $text)</li>")
            continue
        }

        $para.Add($line)
    }

    Close-Para
    Close-Lists 0
    if ($inQuote) { [void]$out.AppendLine('</blockquote>') }
    return $out.ToString()
}

$style = @'
<style>
  @page { size: A4; margin: 18mm 16mm 16mm 16mm; }
  body {
    font-family: "Segoe UI", "Noto Sans", Arial, sans-serif;
    font-size: 10.5pt; line-height: 1.55; color: #1d2129; margin: 0;
  }
  h1, h2, h3, h4 { font-weight: 600; line-height: 1.25; break-after: avoid; }
  h1 { font-size: 24pt; color: #a33a20; margin: 0 0 4pt; }
  h2 { font-size: 15pt; color: #a33a20; margin: 20pt 0 6pt; border-bottom: 1px solid #e3d5cf; padding-bottom: 3pt; }
  h3 { font-size: 12pt; color: #33404d; margin: 14pt 0 4pt; }
  p { margin: 0 0 7pt; }
  ul, ol { margin: 0 0 8pt; padding-left: 18pt; }
  li { margin: 0 0 3pt; break-inside: avoid; }
  li > ul, li > ol { margin-top: 3pt; }
  hr { border: 0; border-top: 1px solid #e6e6e6; margin: 14pt 0; }
  blockquote {
    margin: 10pt 0; padding: 8pt 12pt; background: #f6f1ee;
    border-left: 3px solid #c87a5c; break-inside: avoid;
  }
  blockquote h3 { margin-top: 0; }
  blockquote p:last-child { margin-bottom: 0; }
  code {
    font-family: "Cascadia Mono", Consolas, monospace; font-size: 9.5pt;
    background: #f1f1f4; padding: 1px 4px; border-radius: 3px;
  }
  a { color: #1f5fa8; text-decoration: none; }
  strong { color: #12161c; }
  em { color: #3b4552; }
</style>
'@

function Find-Browser {
    if ($Browser -and (Test-Path $Browser)) { return $Browser }
    $candidates = @(
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw "No headless browser found - install Edge or Chrome, or pass -Browser <path to exe>."
}

# ----------------------------------------------------------------- render

$browserExe = Find-Browser
Write-Host "browser: $browserExe"

foreach ($doc in $documents) {
    $mdPath = Join-Path $RepoRoot $doc.Source
    $pdfPath = Join-Path $RepoRoot $doc.Pdf
    if (-not (Test-Path $mdPath)) { throw "Missing source: $mdPath" }

    $lines = [IO.File]::ReadAllLines($mdPath, [Text.UTF8Encoding]::new($false))
    $title = ($lines | Where-Object { $_ -match '^#\s+(.*)' } | Select-Object -First 1) -replace '^#\s+', ''

    $html = @"
<!DOCTYPE html>
<html><head><meta charset="utf-8"><title>$title</title>$style</head>
<body>
$(Convert-Markdown $lines)
</body></html>
"@

    $htmlPath = [IO.Path]::Combine([IO.Path]::GetTempPath(), [IO.Path]::GetFileNameWithoutExtension($doc.Pdf) + '.html')
    [IO.File]::WriteAllText($htmlPath, $html, [Text.UTF8Encoding]::new($false))

    $profileDir = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'md2pdf-profile')
    $args = @(
        '--headless=new', '--disable-gpu', '--no-first-run', '--no-sandbox',
        "--user-data-dir=$profileDir",
        '--print-to-pdf-no-header',
        "--print-to-pdf=$pdfPath",
        "file:///$($htmlPath -replace '\\', '/')"
    )
    & $browserExe @args | Out-Null
    if (-not (Test-Path $pdfPath)) { throw "Rendering failed for $($doc.Source)" }

    $kb = [math]::Round((Get-Item $pdfPath).Length / 1KB)
    Write-Host ("  {0,-16} -> {1}  ({2} KB)" -f $doc.Source, $doc.Pdf, $kb)
    if ($KeepHtml) { Copy-Item $htmlPath (Join-Path (Split-Path -Parent $pdfPath) ([IO.Path]::GetFileName($htmlPath))) -Force }
    Remove-Item $htmlPath -Force -ErrorAction SilentlyContinue
}

Remove-Item ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'md2pdf-profile')) -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "done"
