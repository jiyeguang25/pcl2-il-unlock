# PCL 2.99.0.0 IL 解锁补丁

恢复 Plain Craft Launcher 2（2.99.0.0 正式版）被移除的 Minecraft 下载功能 —— **在你自己的官方 exe 上做 IL 方法体补丁**，不重新编译整个程序，保留官方版一切原生能力（微软正版登录、CurseForge API Key、CDN 签名、注册表设置、自动更新）。

> ⚠️ 仅供个人学习研究。PCL 版权归龙腾猫跃（LTCatt）所有。官方移除下载功能的说明见 [2.99.0.0 Release Notes](https://github.com/Meloong-Git/PCL/releases/tag/2.99.0.0)。是否使用本工具由使用者自行判断并承担相应责任。

## 原理

官方 2.99.0.0 通过一个编译期常量锁死下载：

```vb
' Plain Craft Launcher 2/Modules/ModSecret.vb 第 18 行
Public Const RestrictMcDownload As Boolean = True
```

常量折叠使 5 个方法体在 IL 层面被"掏空"（换成抛异常/红字提示/隐藏 UI），但**下载核心代码（`DlClientListLoader`、6 个 `InstallPack*` 安装器、1500+ 行多源下载逻辑）在官方 exe 元数据里一行未删**。

本工具用 [dnlib](https://github.com/0xd4d/dnlib) 从一个"未上锁"的同源码构建版（donor）把这 5 个方法体原样移植回官方 exe。donor 方法体引用的每个成员在官方 exe 里都存在（同 commit 编译），因此是纯方法体替换，不动元数据表。

| # | 方法 | 官方（锁定） | 补丁后 |
|---|---|---|---|
| 1 | `PCL.PageDownloadInstall.LoaderInit()` | 假加载器抛"法律法规"异常 | 真 `DlClientListLoader` 加载版本列表 |
| 2 | `PCL.ModModpack.ModpackInstall()` | Hint 红字拦截 | 打开文件选择器安装整合包 |
| 3 | `PCL.ModModpack.ModpackInstall(File,Name,Logo)` | 检测压缩包类型后统一 throw | 完整 switch 分发到 6 个安装器 |
| 4 | `PCL.PageDownloadModify.LoadInstanceFinished(Task)` | 组件卡片 `Collapsed` | 卡片 `Visible` |
| 5 | `PCL.PageDownloadResourceDetail.InitializeStackResourceVersions(Stack)` | 版本列表只有"保存" | 整合包恢复"安装"按钮 |

## 使用方法

### 前置条件（一次性）

1. **补丁工具**：安装 [.NET 10 SDK](https://dotnet.microsoft.com/download) 后构建本仓库：

   ```powershell
   cd MethodPorter
   dotnet build -c Release
   # 产物：MethodPorter\bin\Release\net10.0\MethodPorter.exe
   ```

2. **官方 exe**：你自己的 `Plain Craft Launcher 2.exe`（2.99.0.0 正式版，官方群/网盘下载）。

3. **donor exe**（关键性"钥匙"）：按官方开源仓库源码以 `RestrictMcDownload = False` 构建一次：

   ```powershell
   git clone https://github.com/Meloong-Git/PCL.git --recurse-submodules
   # 编辑 Plain Craft Launcher 2/Modules/ModSecret.vb 第 18 行：
   #   Public Const RestrictMcDownload As Boolean = False
   dotnet build PCLCS\PCLCS.csproj -c Release
   & "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" `
     "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -p:Configuration=Release
   # 产物：Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe 即 donor
   ```

   > VS2022 Build Tools（.NET 桌面生成工作负载）也要装。若 MSBuild 报 `MSB4236 无法解析 Microsoft.NET.Sdk`，把 vbproj 里三个 `ProjectReference` 改为 `Reference` + `HintPath` 指向 `PCLCS\bin` 下的 dll。
   > donor 与官方 exe 必须同 commit 构建，否则方法体签名可能对不上。

### 打补丁

把三样东西放同一目录，运行：

```powershell
.\make-unlocked.ps1
# 产出 unlocked.exe，备份官方 exe 后改名覆盖即可
```

或直接调用工具：

```powershell
MethodPorter.exe <官方exe> <donor exe> <输出exe> port
```

### 回退

把备份的官方 exe 换回去即可。补丁只改 exe 文件本身，不碰注册表与设置。

## 已验证（2.99.0.0 @ Windows 11 26300）

- 补丁版启动稳定，识别码与官方注册表互通（原生继承游戏目录/正版登录/全部设置）
- 下载页版本列表加载器运行正常（Mojang 官方源直连，600ms 左右完成）
- 反编译核对 5 个方法体 IL 与 donor 完全一致（30→18 / 7→41 / 254→330 / 325 / 178→196）

## 注意

- **自动更新会覆盖补丁**：官方 exe 的更新器保留着，官方出新版自动升级后补丁即失效，需对新 exe 重跑（donor 也要用新 tag 源码重建）。介意可在 PCL 设置里关闭自动更新。
- 官方匿名遥测（腾讯云 CLS）同样保留。介意请勿用本工具，改用重编译方案。
- FormMain 残留一处"异常消息恰为拦截串则红字提示"的 catch 分支（永不触发，无害）。
- 每次 PCL 更新（方法签名变化、新增拦截点）都可能需要更新本工具的目标列表（`Program.cs` 里的 `Targets`）。

## 许可

- 本仓库工具代码：MIT
- PCL 及其源码：归原作者所有，本仓库不包含任何 PCL 代码或二进制
