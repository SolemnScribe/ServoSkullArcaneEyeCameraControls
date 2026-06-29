# Build the camera-controls mod and assemble BOTH NexusMods downloads for BOTH games:
#   * Warhammer 40,000: Rogue Trader        -> "Servo-Skull Camera Controls"
#   * Pathfinder: Wrath of the Righteous     -> "Arcane Eye Camera Controls"
#
# The mod's source is identical for both games: it hooks the shared Owlcat camera rig by reflection
# and carries no game-specific or branded strings in its UI. Only the packaged metadata, docs and the
# output assembly NAME differ. We compile the binary once per game with that game's AssemblyName, so
# each DLL's internal identity matches its filename and Info.json. (Shipping one binary renamed on disk
# does NOT work: a .NET assembly's identity is baked in at compile time, so a renamed file still reports
# itself as ServoSkullCameraControls, and UnityModManager - which resolves the mod by that identity -
# refuses to load it, showing a red "!!!" that a restart can't clear. The type namespace stays
# ServoSkullCameraControls either way, so the Info.json EntryMethod 'ServoSkullCameraControls.Main.Load'
# resolves in both builds regardless of the assembly name.)
#
# Per game we produce two zips:
#   1. main mod zip - Info.json + DLL + README + LICENSE        (deployable)
#   2. source zip   - Main.cs + portable .csproj + Info.json + README + LICENSE + this script
#
# Prereq: the .NET SDK (dotnet) on PATH, and in $dev: Main.cs, LICENSE, the working .csproj, and
# both games' Info/README files (see $targets below). Edit $dev if your working copy lives elsewhere.

$ErrorActionPreference = 'Stop'
$dev = "$env:USERPROFILE\Dev\ServoSkullCameraControls"
Set-Location $dev

# --- Per-game packaging targets. Source is shared; the build (assembly name), metadata and docs differ.
#   Base       : zip base name + staged folder name
#   Dll        : file name this game's compiled binary is shipped as in the main zip
#   InfoFile   : Info.json source in $dev (renamed to Info.json inside the package)
#   ReadmeFile : README source in $dev   (renamed to README.md inside the package)
#   AsmName    : <AssemblyName> for this game - used both to compile the main binary and written into
#                the source-zip .csproj, so the DLL's name/identity matches this game's Info.json
#   GameDir/DataDir/UmmDir : genericized install paths written into the source-zip .csproj
$targets = @(
    [ordered]@{
        Name       = 'Servo-Skull Camera Controls (Rogue Trader)'
        Base       = 'ServoSkullCameraControls'
        Dll        = 'ServoSkullCameraControls.dll'
        InfoFile   = 'Info.json'
        ReadmeFile = 'README.md'
        AsmName    = 'ServoSkullCameraControls'
        GameDir    = 'C:\Program Files (x86)\Steam\steamapps\common\Warhammer 40,000 Rogue Trader'
        DataDir    = 'WH40KRT_Data'
        UmmDir     = '$(USERPROFILE)\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager'
    },
    [ordered]@{
        Name       = 'Arcane Eye Camera Controls (Wrath of the Righteous)'
        Base       = 'ArcaneEyeCameraControls'
        Dll        = 'ArcaneEyeCameraControls.dll'
        InfoFile   = 'Info.ArcaneEye.json'
        ReadmeFile = 'README.ArcaneEye.md'
        AsmName    = 'ArcaneEyeCameraControls'
        GameDir    = 'C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure'
        DataDir    = 'Wrath_Data'
        UmmDir     = '$(USERPROFILE)\AppData\LocalLow\Owlcat Games\Pathfinder Wrath Of The Righteous\UnityModManager'
    }
)

# Clear any staging area from a previous run BEFORE building. It lives inside the project folder, so a
# leftover copy of Main.cs there would be compiled alongside the real one by the SDK default glob
# (two definitions of every type -> CS0101/CS0111). The .csproj also excludes package\** from the
# build, so this is belt-and-suspenders, but it keeps the tree clean too.
Remove-Item "$dev\package" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$dev\build"   -Recurse -Force -ErrorAction SilentlyContinue

# Base .csproj that we genericize per game for the source zips.
$csprojBase = Get-Content "$dev\ServoSkullCameraControls.csproj" -Raw

foreach ($t in $targets) {
    Write-Host "`nPackaging $($t.Name)..." -ForegroundColor Cyan

    if (-not (Test-Path "$dev\$($t.InfoFile)"))   { throw "Missing $($t.InfoFile) in $dev." }
    if (-not (Test-Path "$dev\$($t.ReadmeFile)")) { throw "Missing $($t.ReadmeFile) in $dev." }

    $ver = (Get-Content "$dev\$($t.InfoFile)" -Raw | ConvertFrom-Json).Version

    # Compile this game's binary with its own assembly name into an isolated output folder. RootNamespace
    # (and so every type's namespace) stays ServoSkullCameraControls, so the Info.json EntryMethod still
    # resolves; only the assembly's name/identity becomes $($t.AsmName), matching its filename and Info.json
    # so UnityModManager loads it cleanly.
    $outDir = "$dev\build\$($t.Base)"
    dotnet build -c Release -p:AssemblyName=$($t.AsmName) -o "$outDir"
    $built = Get-ChildItem "$outDir" -Filter "$($t.Dll)" | Select-Object -First 1
    if (-not $built) { throw "Build finished but $($t.Dll) was not found in $outDir." }

    # --- 1. Main download: the deployable mod folder ---
    $stage = "$dev\package\$($t.Base)"
    New-Item -ItemType Directory $stage -Force | Out-Null
    Copy-Item $built.FullName          "$stage\$($t.Dll)"      # this game's binary; assembly name matches the file
    Copy-Item "$dev\$($t.InfoFile)"    "$stage\Info.json"
    Copy-Item "$dev\$($t.ReadmeFile)"  "$stage\README.md"
    Copy-Item "$dev\LICENSE"           $stage
    Copy-Item "$dev\Localization"      $stage -Recurse          # bundled UI translations (en + 10 languages)

    $mainZip = "$dev\$($t.Base)-$ver.zip"
    Remove-Item $mainZip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path $stage -DestinationPath $mainZip

    # --- 2. Source download: code + portable project + docs + this script ---
    $srcStage = "$dev\package\$($t.Base)-source"
    New-Item -ItemType Directory $srcStage -Force | Out-Null
    Copy-Item "$dev\Main.cs"           $srcStage
    Copy-Item "$dev\$($t.InfoFile)"    "$srcStage\Info.json"
    Copy-Item "$dev\$($t.ReadmeFile)"  "$srcStage\README.md"
    Copy-Item "$dev\LICENSE"           $srcStage
    Copy-Item "$dev\Localization"      $srcStage -Recurse       # ship the locale files with the source too
    Copy-Item $PSCommandPath           "$srcStage\build-and-package.ps1"

    # Genericize the machine-specific paths and set the output assembly name so a from-source build
    # produces a DLL whose name matches this game's Info.json. The Managed sub-path is the game's
    # *_Data folder. Everything else in the project (references / HintPaths) is preserved verbatim.
    # Replacement values are passed through String.Replace('$','$$') because '$' is special in a
    # regex replacement ('$$' = a literal '$'), so MSBuild props like $(GameDir) survive intact.
    $managed = "`$(GameDir)\$($t.DataDir)\Managed"
    $edits = [ordered]@{
        '(?s)<AssemblyName>.*?</AssemblyName>' = "<AssemblyName>$($t.AsmName)</AssemblyName>"
        '(?s)<GameDir>.*?</GameDir>'           = "<GameDir>$($t.GameDir)</GameDir>"
        '(?s)<Managed>.*?</Managed>'           = "<Managed>$managed</Managed>"
        '(?s)<UMMDir>.*?</UMMDir>'             = "<UMMDir>$($t.UmmDir)</UMMDir>"
    }
    $csproj = $csprojBase
    foreach ($pat in $edits.Keys) {
        $repl = $edits[$pat].Replace('$', '$$')      # escape '$' for the regex replacement string
        $csproj = $csproj -replace $pat, $repl
    }
    if ($csproj -match [regex]::Escape($env:USERPROFILE)) {
        Write-Warning "Source .csproj for $($t.Base) still contains your user path - check the <GameDir>/<Managed>/<UMMDir> elements weren't reformatted."
    }
    [System.IO.File]::WriteAllText("$srcStage\$($t.Base).csproj", $csproj)

    $srcZip = "$dev\$($t.Base)-$ver-source.zip"
    Remove-Item $srcZip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path $srcStage -DestinationPath $srcZip

    Write-Host "  Main download:   $mainZip"  -ForegroundColor Green
    Write-Host "  Source download: $srcZip"   -ForegroundColor Green
}

Write-Host "`nDone - two games, four zips." -ForegroundColor Green
