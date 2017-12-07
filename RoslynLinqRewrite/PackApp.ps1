param($inputexe)

if(-not $inputexe){
    $candidates = @(dir *.exe)
    if($candidates.Count -ne 1){ throw 'Cannot find an unambiguous executable in the current directory.' }
    $inputexe = $candidates[0].FullName
}

$inputexe = (Get-Item $inputexe).FullName
$inputdir = (Get-Item $inputexe).Directory.FullName
$appname = (Get-Item $inputexe).BaseName

$outputdir = [IO.Path]::Combine($inputdir, $appname)
[IO.Directory]::CreateDirectory($outputdir) > $null
$inputexename = [IO.Path]::GetFileName($inputexe)
function IsNetDll($path){
    $name = [IO.Path]::GetFileName($path)
    if($name -eq $inputexename){ return $false }
    #if($name -eq 'netstandard.dll'){ return $true }
    if($name -eq 'protobuf-net.dll'){ return $true }
    if($name -eq 'shaman.tld.dll'){ return $true }
    if($name -eq 'csc.dll'){ return $true }
    if([IO.File]::Exists([IO.Path]::ChangeExtension($path, '.jar'))){ return $true }
    
    if($name -eq 'System.Net.Http.dll'){ return $false }
    if($name.EndsWith('Native.amd64.dll')){ return $false }
    if($name.EndsWith('Native.x86.dll')){ return $false }
    if($name.EndsWith('Native.arm.dll')){ return $false }
    
    if(-not $name.EndsWith('.dll')){ return $false }
    if([char]::IsUpper($name[0])){ return $true }
    return $false
}

$dlls = (dir $inputdir\*.dll | where{(IsNetDll $_.FullName)} | SelectSimple{$_.FullName})

$outfilename = "$appname.dll"
if([IO.Path]::GetExtension($inputexe) -eq '.exe') { $outfilename = "$appname.exe" }

ilrepack /out:$outputdir\$outfilename $inputexe $dlls /lib:$inputdir $args


dir $inputdir\* | where { -not $_.PSIsContainer -and -not (IsNetDll $_.FullName) -and -not $_.Name.Contains('runtimeconfig.') -and -not ($_.Name.EndsWith('.config')) -and ($_.Name -ne 'netstandard.dll') -and ($_.Extension -ne '.pdb') -and ($_.Extension -ne '.tmp') -and ($_.FullName -ne $inputexe) -and -not ($_.Extension -eq '.xml' -and [char]::IsUpper($_.Name[0]))} | %{ copy $_.FullName $outputdir -Force }



