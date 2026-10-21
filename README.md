# PCL2 官方版一键解锁补丁器

**恢复 Plain Craft Launcher 2（2.99.0.0 正式版）被移除的 Minecraft 下载功能**——把 `PCL2UnlockPatcher.exe` 和官方 `Plain Craft Launcher 2.exe` 放在同一个文件夹，双击补丁器，完成。

> ⚠️ 仅供个人学习研究。PCL 版权归龙腾猫跃（LTCatt）所有，本工具不含任何 PCL 代码或二进制（内嵌的参考构建由官方开源仓库同 commit 源码编译，不含任何凭据）。是否使用由使用者自行判断并承担相应责任。

## 使用方法（三步）

1. 下载 `PCL2UnlockPatcher.exe`（Releases 或自建，见下）
2. 把它和你电脑上的官方 PCL2 主程序 `Plain Craft Launcher 2.exe`（**2.99.0.0 正式版**）放进同一个空文件夹
3. 双击 `PCL2UnlockPatcher.exe`

补丁器会自动：识别官方 exe → 释放内嵌参考构建 → 移植 5 个被锁方法体 → 备份原版（`.bak-时间戳`）→ 原地替换。完成后直接启动 PCL2 即可。

- **不需要** .NET Core / .NET 10 运行时（补丁器是 .NET Framework 4.8 程序，Win10/11 自带）
- **不需要** 构建环境、源码、命令行——全部自动
- **保留** 官方版一切原生能力：微软正版登录、CurseForge API Key、CDN 签名、你的全部设置与游戏目录、自动更新器
- 想还原：把 `.bak` 文件改回原名即可，无任何残留

## 原理

官方 2.99.0.0 用一个编译期常量锁死了下载：

```vb
' Plain Craft Launcher 2/Modules/ModSecret.vb 第 18 行
Public Const RestrictMcDownload As Boolean = True
```

常量折叠使 5 个方法体在 IL 层面被"掏空"（换成抛异常/红字提示/隐藏 UI），但**下载核心代码（`DlClientListLoader`、6 个 `InstallPack*` 安装器等 1500+ 行）在官方 exe 元数据里一行未删**。

补丁器内嵌一个"干净参考构建"（donor）：官方开源仓库同 commit（`6b9a50d`）源码、仅把上述常量改为 `False`、不含任何凭据的编译产物（Deflate 压缩后 2.5MB）。用 dnlib 把 donor 的 5 个方法体移植回官方 exe——donor 方法体引用的每个成员在官方 exe 元数据里都存在（同 commit 编译），因此是纯方法体替换，不碰元数据表：

| # | 方法 | 官方（锁定） | 补丁后 |
|---|---|---|---|
| 1 | `PCL.PageDownloadInstall.LoaderInit()` | 假加载器抛"法律法规"异常 | 真 `DlClientListLoader` 加载版本列表 |
| 2 | `PCL.ModModpack.ModpackInstall()` | Hint 红字拦截 | 打开文件选择器安装整合包 |
| 3 | `PCL.ModModpack.ModpackInstall(File,Name,Logo)` | 检测压缩包类型后统一 throw | 完整 switch 分发到 6 个安装器 |
| 4 | `PCL.PageDownloadModify.LoadInstanceFinished(Task)` | 组件卡片 `Collapsed` | 卡片 `Visible` |
| 5 | `PCL.PageDownloadResourceDetail.InitializeStackResourceVersions(Stack)` | 版本列表只有"保存" | 整合包恢复"安装"按钮 |

## 自己构建补丁器（可选）

需要任意能编译 .NET Framework 4.8 的环境（.NET SDK 或 VS）：

```powershell
git clone https://github.com/jiyeguang25/pcl2-il-unlock.git
cd pcl2-il-unlock\Patcher
dotnet build -c Release
# 产物：bin\Release\net48\PCL2UnlockPatcher.exe（约 3.8MB 单文件）
```

`donor.deflate`（内嵌参考构建的压缩流）已随仓库提供；若想从源码重建 donor，方法见[附录](#附录重建-donor)。

## 重建 donor / 适配新版本（进阶）

官方出新版本后，本补丁器的目标列表（`Patcher/Program.cs` 里的 `Targets`）可能需要更新；donor 也要用新 tag 源码重建：

1. 下载官方开源仓库对应 tag 源码（含 `MeloongCore` 子模块）
2. `Modules/ModSecret.vb` 把 `RestrictMcDownload` 改为 `False`（唯一功能改动）
3. 构建辅助（若本机缺 .NET Framework 4.8 Developer Pack / VS BuildTools）：
   - `PCLCS.csproj` 加 `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 包引用
   - 主 vbproj 三个 `ProjectReference` 改为 `Reference+HintPath` 指向 `PCLCS\bin` 下的 dll
4. `dotnet build PCLCS\PCLCS.csproj -c Release` → VS MSBuild 编译主 vbproj
5. 产物 exe 用 Deflate 压缩，替换 `Patcher/donor.deflate`，重新编译补丁器
6. 按 `Targets` 列表核对新版方法签名（ilspycmd 反编译对照）

## 已验证（2.99.0.0 @ Windows 11 26300）

- 补丁器单文件 3.8MB（dnlib + donor 全内嵌，零旁挂文件）
- 官方原版（SHA256 `6B6E1260...`）→ 补丁一次成功，5 个方法体 IL 指令数与 donor 完全一致（30→18 / 7→41 / 254→330 / 325 / 178→196）
- 补丁版启动稳定，识别码与官方注册表互通，原生继承游戏目录与已装实例
- 下载页版本列表加载器运行正常（Mojang 官方源直连，约 650ms 完成）
- 官方 exe 未被修改注册表或任何系统状态；还原 = 改回 `.bak` 文件名

## 注意

- **自动更新会覆盖补丁**：官方 exe 的更新器被保留，官方推送新版本并自动升级后补丁即失效，需用新版补丁器重打（或关闭 PCL 的自动更新）。
- 官方匿名遥测（腾讯云 CLS）同样保留。介意请勿使用本工具。
- 本工具只改 exe 文件本身，不碰注册表、不联网、不采集任何数据。

## 许可

- 本仓库工具代码：MIT
- PCL 及其源码：归原作者所有；本仓库不含 PCL 代码/二进制；内嵌 donor 为开源码编译产物，按《PCL 分发有限许可》以第三方二次创作名义分发，特此署名：**龙腾猫跃（LTCatt）**，[PCL 官方仓库](https://github.com/Meloong-Git/PCL)

## 附录：重建 donor

见上一节步骤 2-4。构建命令摘要：

```powershell
# C# 侧（产物进 PCLCS\bin\）
dotnet build PCLCS\PCLCS.csproj -c Release
# VB 主程序
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" `
  "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -p:Configuration=Release
# 压缩为 donor.deflate（.NET DeflateStream，Optimal）
```
