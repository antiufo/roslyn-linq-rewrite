@echo off
cd /d %~dp0

if exist RoslynLinqRewrite\bin\Debug\roslyn-linq-rewrite.exe (
    copy RoslynLinqRewrite\bin\Debug\roslyn-linq-rewrite.exe RoslynLinqRewrite\bin\Debug\csc.exe /y
    copy RoslynLinqRewrite\bin\Debug\roslyn-linq-rewrite.exe.config RoslynLinqRewrite\bin\Debug\csc.exe.config /y
)
if exist RoslynLinqRewrite\bin\Release\roslyn-linq-rewrite.exe (
    copy RoslynLinqRewrite\bin\Release\roslyn-linq-rewrite.exe RoslynLinqRewrite\bin\Release\csc.exe /y
    copy RoslynLinqRewrite\bin\Release\roslyn-linq-rewrite.exe.config RoslynLinqRewrite\bin\Release\csc.exe.config /y
)
