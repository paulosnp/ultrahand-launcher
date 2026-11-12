# Lists the launcher's user-facing English texts and the ones Translations.cs has no entry for.
#   powershell -File tools\check-translations.ps1            missing entries (exit code 1 when there are any)
#   powershell -File tools\check-translations.ps1 -All       every text found, one per line
# Texts come from Tr.T / Tr.F calls in the .cs files, the Text / Content / ToolTip attributes of the .xaml files, and the labels,
# descriptions and choice names of GameSettings.cs. The English text is the key (Localization.cs).
param([switch]$All)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot '..\UltrahandLauncher'

# Texts that stay as they are in every language
$keep = @('Ultrahand', 'Ultrahand Launcher', 'prod.keys', '   |   ', '0%', 'THE LEGEND OF ZELDA', 'Tears of the Kingdom', 'OK', 'FPS', 'PC',
          'Xbox', 'Nintendo', 'HDR', 'Mailbox', 'VSync', 'AMD FSR 1', 'NVIDIA DLSS / DLAA', 'Discord Rich Presence', 'English',
          'Русский', 'Português (Brasil)', '简体中文', 'Español', 'frame rate', '30 FPS', '40 FPS', '45 FPS', '60 FPS', '90 FPS',
          '120 FPS', '144 FPS', '50 %', '67 %', '75 %', '100 %', '125 %', '150 %', '200 %', '2x', '4x', '8x', '16x', '960 (1x)',
          '1920 (2x)', '2880 (3x)', '3840 (4x)', 'Shift', 'Alt', 'Ctrl', 'Tab', 'Bilinear',
          # (not words: the game language codes, the totk.ini writer's pieces, a step state)
          'en-GB', 'en-US', 'fr-CA', 'pt-BR', 'zh-CN', 'zh-Hans', 'zh-Hant', 'zh-TW', ' = ', ' × ', '# key = value', 'key = value', '[{section}]', '\r\n', 'current', '', '160 m (1x)', '240 m (1.5x)', '320 m (2x)', '480 m (3x)')
$keep += "\r
"   # (the totk.ini writer's line ending, as the extractor reads it)

function Unescape([string]$s) { $s.Replace('\"', '"').Replace('\n', "`n").Replace('\\', '\') }

# The string literal groups of C# code: "a" + "b" joined, with where each group ends
function Groups([string]$code) {
  $out = New-Object System.Collections.Generic.List[object]
  $rx = [regex]'"((?:[^"\\\r\n]|\\.)*)"'
  $m = $rx.Match($code)
  while ($m.Success) {
    $text = $m.Groups[1].Value; $end = $m.Index + $m.Length
    while ($true) {
      $rest = $code.Substring($end)
      $join = [regex]::Match($rest, '^\s*\+\s*"((?:[^"\\\r\n]|\\.)*)"')
      if (-not $join.Success) { break }
      $text += $join.Groups[1].Value; $end += $join.Length
    }
    $out.Add([pscustomobject]@{ Text = (Unescape $text); Start = $m.Index; End = $end })
    $m = $rx.Match($code, $end)
  }
  $out
}

$found = New-Object System.Collections.Generic.HashSet[string]

# 1. Tr.T( / Tr.F( calls: the literal groups inside, except switch patterns ("x" =>) and, for Tr.F, all but the first
foreach ($file in Get-ChildItem $src -Filter *.cs) {
  $code = [IO.File]::ReadAllText($file.FullName)
  $groups = Groups $code
  foreach ($call in [regex]::Matches($code, 'Tr\.(T|F)\(')) {
    $depth = 1; $i = $call.Index + $call.Length
    while ($i -lt $code.Length -and $depth -gt 0) {
      $ch = $code[$i]
      if ($ch -eq '"') { $g = $groups | Where-Object { $_.Start -eq $i } | Select-Object -First 1; if ($g) { $i = $g.End; continue } }
      if ($ch -eq '(') { $depth++ } elseif ($ch -eq ')') { $depth-- }
      $i++
    }
    $inside = $groups | Where-Object { $_.Start -gt $call.Index -and $_.End -le $i }
    if ($call.Groups[1].Value -eq 'F') { $inside = @($inside | Select-Object -First 1) }
    foreach ($g in $inside) {
      if ($code.Substring($g.End) -match '^\s*=>') { continue }
      [void]$found.Add($g.Text)
    }
  }
}

# 2. Static XAML texts
foreach ($file in Get-ChildItem $src -Filter *.xaml) {
  foreach ($m in [regex]::Matches([IO.File]::ReadAllText($file.FullName), '\b(Text|Content|ToolTip)="([^"{][^"]*)"')) {
    [void]$found.Add([System.Net.WebUtility]::HtmlDecode($m.Groups[2].Value))
  }
}

# 3. GameSettings.cs and the game language names: texts that read as words, not keys, values or formats
foreach ($name in 'GameSettings.cs', 'Installer.cs', 'MainWindow.xaml.cs', 'MainWindow.Settings.cs') {
  $code = [IO.File]::ReadAllText((Join-Path $src $name))
  if ($name -eq 'Installer.cs') { $code = [regex]::Match($code, '(?s)static class GameLanguages.*').Value }
  if ($name -eq 'MainWindow.xaml.cs') { $code = [regex]::Match($code, '(?s)StepNames = new\(\).*?\};').Value + [regex]::Match($code, '(?s)foreach \(var \(box, need, missing\).*?\}\)').Value }
  if ($name -eq 'MainWindow.Settings.cs') { $code = [regex]::Match($code, 'LauncherTabs = \{[^}]*\}').Value + ' ' + (([regex]::Matches($code, 'Heading\("[^"]*"\)|SmallButton\("[^"]*"') | ForEach-Object Value) -join ' ') }
  foreach ($g in (Groups $code)) {
    $s = $g.Text
    if ($s.Length -lt 2 -or $s.StartsWith('{') -or $s -cmatch '^[a-z0-9_.\-]+$') { continue }
    if ($name -eq 'MainWindow.xaml.cs' -and $code.Substring(0, $g.Start) -match '\[\s*$') { continue }   # (the step ids)
    [void]$found.Add($s)
  }
}

$keys = New-Object System.Collections.Generic.HashSet[string]
$tablePath = Join-Path $src 'Translations.cs'
if (Test-Path $tablePath) {
  foreach ($m in [regex]::Matches([IO.File]::ReadAllText($tablePath), '\["((?:[^"\\]|\\.)*)"\]\s*=')) { [void]$keys.Add((Unescape $m.Groups[1].Value)) }
}

$wanted = $found | Where-Object { $keep -notcontains $_ -and $_ -match '\p{L}' } | Sort-Object
if ($All) { $wanted | ForEach-Object { $_.Replace("`n", '\n') }; return }
$missing = @($wanted | Where-Object { -not $keys.Contains($_) })
$unused = @($keys | Where-Object { $wanted -notcontains $_ })
"texts: $(@($wanted).Count), translated: $(@($wanted).Count - $missing.Count), missing: $($missing.Count), unused entries: $($unused.Count)"
$missing | ForEach-Object { "  missing: " + $_.Replace("`n", '\n') }
$unused | ForEach-Object { "  unused:  " + $_.Replace("`n", '\n') }
if ($missing.Count) { exit 1 }
