param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [string]$CompilerPath=(Join-Path $env:ProgramFiles 'Unity\Hub\Editor\2020.3.23f1\Editor\Data\Tools\Roslyn\csc.exe')
)
$ErrorActionPreference='Stop'
$GamePath=(Resolve-Path -LiteralPath $GamePath).Path
if(-not(Test-Path -LiteralPath $CompilerPath -PathType Leaf)){
    throw 'Unity Roslyn csc.exe was not found. Pass its full path with -CompilerPath.'
}
$managed=Join-Path $GamePath 'A Township Tale_Data\Managed'
if(-not(Test-Path -LiteralPath (Join-Path $managed 'Root.Township.dll') -PathType Leaf)){
    throw 'Pass the A Township Tale installation directory with -GamePath.'
}
$dependencies=@('MelonLoader\net35\MelonLoader.dll','MelonLoader\net35\0Harmony.dll','Mods\SyncLib.dll')
foreach($dependency in $dependencies){
    if(-not(Test-Path -LiteralPath (Join-Path $GamePath $dependency) -PathType Leaf)){
        throw "Missing $dependency in the game directory. Install MelonLoader and SyncLib before building."
    }
}
$bundle=Join-Path $PSScriptRoot 'Builds\player-iron-armor'
$standBundle=Join-Path $PSScriptRoot 'Builds\player-armor-stand'
$wyrmBundle=Join-Path $PSScriptRoot 'Builds\player-wyrm-armor'
$workshopBundle=Join-Path $PSScriptRoot 'Builds\player-armor-workshop'
$jigPlans=Join-Path $PSScriptRoot 'Builds\player-armor-jig-plans.json'
foreach($asset in @($bundle,$standBundle,$wyrmBundle,$workshopBundle,$jigPlans)){
    if(-not(Test-Path -LiteralPath $asset -PathType Leaf)){
        throw "Missing build resource: $asset. Restore it from the repository."
    }
}
$recipeGlyphDirectory=Join-Path $PSScriptRoot 'Builds\RecipeGlyphs'
$recipeGlyphNames=@(
    'ATT_IronArmor_Helmet','ATT_IronArmor_Cuirass',
    'ATT_IronArmor_Pauldron_Left','ATT_IronArmor_Pauldron_Right','ATT_IronArmor_Bracer_Left','ATT_IronArmor_Bracer_Right','ATT_IronArmor_Tassets',
    'ATT_LeatherArmor_Helmet','ATT_LeatherArmor_Cuirass',
    'ATT_LeatherArmor_Pauldron_Left','ATT_LeatherArmor_Pauldron_Right','ATT_LeatherArmor_Bracer_Left','ATT_LeatherArmor_Bracer_Right','ATT_LeatherArmor_Tassets',
    'ATT_WoodlandArmor_Helmet','ATT_WoodlandArmor_Cuirass','ATT_WoodlandArmor_Pauldron_Left','ATT_WoodlandArmor_Pauldron_Right','ATT_WoodlandArmor_Bracer_Left','ATT_WoodlandArmor_Bracer_Right','ATT_WoodlandArmor_Tassets',
    'ATT_HebiosArmor_Helmet','ATT_HebiosArmor_Cuirass','ATT_HebiosArmor_Pauldron_Left','ATT_HebiosArmor_Pauldron_Right','ATT_HebiosArmor_Bracer_Left','ATT_HebiosArmor_Bracer_Right','ATT_HebiosArmor_Tassets',
    'ATT_MushroomArmor_Helmet','ATT_MushroomArmor_Cuirass','ATT_MushroomArmor_Pauldron_Left','ATT_MushroomArmor_Pauldron_Right','ATT_MushroomArmor_Bracer_Left','ATT_MushroomArmor_Bracer_Right','ATT_MushroomArmor_Tassets'
)
if(-not(Test-Path -LiteralPath $recipeGlyphDirectory -PathType Container)){throw 'Restore Builds\RecipeGlyphs from the repository.'}
$recipeGlyphFiles=@(Get-ChildItem -LiteralPath $recipeGlyphDirectory -Filter '*.png' -File)
$recipeGlyphSet=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($recipeGlyphFile in $recipeGlyphFiles){[void]$recipeGlyphSet.Add($recipeGlyphFile.BaseName)}
if($recipeGlyphFiles.Count -ne 35 -or -not $recipeGlyphSet.SetEquals([string[]]$recipeGlyphNames)){throw 'Builds\RecipeGlyphs must contain exactly the 35 prefab-named approved recipe PNGs.'}
foreach($recipeGlyphName in $recipeGlyphNames){
    $recipeGlyphPath=Join-Path $recipeGlyphDirectory ($recipeGlyphName+'.png')
    if(-not(Test-Path -LiteralPath $recipeGlyphPath -PathType Leaf) -or (Get-Item -LiteralPath $recipeGlyphPath).Length -eq 0){throw ('Missing/empty approved recipe glyph: '+$recipeGlyphName)}
}
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'bin') -Force | Out-Null
foreach($role in @('Client','Server')){
    $sources=@(Join-Path $PSScriptRoot 'AssemblyInfo.cs';Join-Path $PSScriptRoot 'Core.cs')
    foreach($folder in @('Shared','Runtime',$role)){$sources+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $folder) -Filter '*.cs' | ForEach-Object {$_.FullName}}
    $arguments=@('/nologo','/target:library','/langversion:latest','/nostdlib+','/optimize+',('/define:'+$role.ToUpperInvariant()),('/out:"'+(Join-Path $PSScriptRoot ('bin\PlayerArmor.'+$role+'.dll'))+'"'))
    $arguments+='/resource:"'+$bundle+'",PlayerArmor.Model'
    $arguments+='/resource:"'+$standBundle+'",PlayerArmor.Stand'
    $arguments+='/resource:"'+$wyrmBundle+'",PlayerArmor.Wyrm'
    $arguments+='/resource:"'+$workshopBundle+'",PlayerArmor.Workshop'
    $arguments+='/resource:"'+$jigPlans+'",PlayerArmor.JigPlans'
    if($role -eq 'Client'){
        foreach($recipeGlyphName in $recipeGlyphNames){$arguments+='/resource:"'+(Join-Path $recipeGlyphDirectory ($recipeGlyphName+'.png'))+'",PlayerArmor.Glyphs.'+$recipeGlyphName+'.png'}
    }
    $arguments+=Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File | ForEach-Object {'/reference:"'+$_.FullName+'"'}
    foreach($dependency in $dependencies){$arguments+='/reference:"'+(Join-Path $GamePath $dependency)+'"'}
    $arguments+=$sources | ForEach-Object {'"'+$_+'"'}
    $response=Join-Path $PSScriptRoot ('bin\'+$role+'.rsp');Set-Content -LiteralPath $response -Value $arguments -Encoding utf8
    & $CompilerPath ('@'+$response)
    if($LASTEXITCODE -ne 0){throw "$role native compilation failed"}
}
