param([switch]$Tests)
$ErrorActionPreference='Stop'
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out=Join-Path $PSScriptRoot 'bin'
[void][IO.Directory]::CreateDirectory($out)
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf=Join-Path $framework 'WPF'
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll') | ForEach-Object {'/reference:'+ (Join-Path $framework $_)}
$refs+=@('PresentationCore.dll','WindowsBase.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object {'/reference:'+(Join-Path $wpf $_)}
$files=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
$exe=Join-Path $out 'INTEX ReadAloud Enhancer v0.12.exe'
$options=@('/nologo','/target:winexe','/platform:x64','/optimize+','/debug:pdbonly','/codepage:65001',('/out:'+$exe),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/win32icon:'+(Join-Path $PSScriptRoot 'assets\INTEX ReadAloud Enhancer.ico')))
& $compiler @options @refs @files
if($LASTEXITCODE -ne 0){throw 'Blad kompilacji C#.'}
"Zbudowano: $exe"
