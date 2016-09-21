@echo off
cd /d %~dp0

dotnet restore dotnet-compile-csc-linq-rewrite
if not exist RoslynLinqRewrite\bin\Debug mkdir RoslynLinqRewrite\bin\Debug
if not exist RoslynLinqRewrite\bin\Release mkdir RoslynLinqRewrite\bin\Release


copy /y %userprofile%\.nuget\packages\System.Linq\4.1.0\lib\net463\*.dll RoslynLinqRewrite\bin\Debug 
copy /y %userprofile%\.nuget\packages\System.Linq\4.1.0\lib\net463\*.dll RoslynLinqRewrite\bin\Release

copy /y %userprofile%\.nuget\packages\System.Runtime.Loader\4.0.0\lib\netstandard1.5\*.dll RoslynLinqRewrite\bin\Debug 
copy /y %userprofile%\.nuget\packages\System.Runtime.Loader\4.0.0\lib\netstandard1.5\*.dll RoslynLinqRewrite\bin\Release

copy /y %userprofile%\.nuget\packages\Microsoft.DiaSymReader.native\1.5.0-beta1\runtimes\win\native\*.dll RoslynLinqRewrite\bin\Debug
copy /y %userprofile%\.nuget\packages\Microsoft.DiaSymReader.native\1.5.0-beta1\runtimes\win\native\*.dll RoslynLinqRewrite\bin\Release


