
$config = 'Release'

msbuild "$pwd\RoslynLinqRewrite" /p:Configuration=$config
C:\Repositories\Scripts\PackApp.ps1 "$pwd\RoslynLinqRewrite\bin\$config\roslyn-linq-rewrite.exe" /keyfile:..\ShamanOpenSourceKey.snk /allowdup:System.ValueTuple "/allowdup:System.*"
copy -Force "CscWrapper\bin\Debug\csc-wrapper.exe" "RoslynLinqRewrite\bin\$config\roslyn-linq-rewrite\csc.exe"
copy -Force "Libs\csc.dll" "RoslynLinqRewrite\bin\$config\roslyn-linq-rewrite"
copy -Force "RoslynLinqRewrite\bin\$config\roslyn-linq-rewrite\roslyn-linq-rewrite.exe.config" "RoslynLinqRewrite\bin\$config\csc.exe.config"
copy -Force "RoslynLinqRewrite\bin\$config\roslyn-linq-rewrite\roslyn-linq-rewrite.exe.config" "RoslynLinqRewrite\bin\$config\roslyn-linq-rewrite\csc.exe.config"

if($config -eq 'Release'){
    $zip = "$pwd\roslyn-linq-rewrite.zip"
    if(Test-Path $zip){ del $zip }
    $old = $pwd
    cd "RoslynLinqRewrite\bin\$config\"
    7z a $zip "roslyn-linq-rewrite"
    cd $old
}