# 一键补丁脚本：官方 PCL 2.99.0.0 exe + donor exe → unlocked.exe
# 前置：已按仓库 README 构建 MethodPorter（dotnet build -c Release）与 donor exe
# 用法：把官方 exe 与 donor exe 放本目录，运行本脚本
$ErrorActionPreference = "Stop"
$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$tool  = Join-Path $here "MethodPorter\bin\Release\net10.0\MethodPorter.exe"
$off   = Join-Path $here "Plain Craft Launcher 2.exe"          # 官方 2.99.0.0 正式版原版
$donor = Join-Path $here "donor-Plain Craft Launcher 2.exe"     # RestrictMcDownload=False 构建版
$out   = Join-Path $here "unlocked.exe"

if (-not (Test-Path $tool))  { throw "找不到补丁工具，请先在 MethodPorter 目录 dotnet build -c Release" }
if (-not (Test-Path $off))   { throw "找不到官方 Plain Craft Launcher 2.exe" }
if (-not (Test-Path $donor)) { throw "找不到 donor-Plain Craft Launcher 2.exe（构建方法见 README）" }

# 校验官方 exe 版本（防错补）
$an = [System.Reflection.AssemblyName]::GetAssemblyName($off)
if ($an.Version.ToString(4) -ne "2.99.0.0") { throw "官方 exe 版本应为 2.99.0.0，实际 $($an.Version)（新版本需更新工具目标列表）" }

& $tool $off $donor $out port
if (-not (Test-Path $out)) { throw "补丁失败：未生成 unlocked.exe" }

Write-Host ""
Write-Host "完成：$out"
Write-Host "SHA256: $((Get-FileHash $out -Algorithm SHA256).Hash)"
Write-Host "请先备份官方 exe，再将 unlocked.exe 改名为 'Plain Craft Launcher 2.exe' 覆盖。"
