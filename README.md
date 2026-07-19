# SHBT — SIMATIC Hot Backup Tool

[中文](#中文) | [English](#english)

---

## 中文

### 简介

**SHBT（SIMATIC Hot Backup Tool）** 是一款面向西门子自动化工程的**热备份工具**。无需停机、不锁定文件、不触碰授权——通过 Windows 卷影复制（VSS）快照工程目录，再用 7-Zip 高压缩归档到指定目标盘。

### 支持的工程类型

| 类型 | 识别依据（目录 或 文件，命中其一即可） |
|------|----------|
| **TIA Portal** | `XRef` 目录 或 `*.ap1x` 文件（如 `.ap15`/`.ap19`） |
| **STEP7 V5.X** | `AMOBJS` 目录 或 `*.s7p` 工程文件 |
| **WinCC Classic (v7.x/v8.x)** | `GRACS` / `ArchiveManager` 目录 或 `*.mcp` 工程文件 |

### 核心特性

- **零停机备份**：VSS 快照确保文件一致性，工程在备份期间正常运行
- **多目标并行**：一次备份可同时写入多个目标盘（勾选即可）
- **灵活排除规则**：自定义忽略文件/目录模式（`-xr!` 传递给 7-Zip）
- **安全保护**：自动识别授权/加密狗盘并默认禁止写入，需显式确认
- **磁盘空间预检**：启动前估算源目录大小并逐目标盘校验可用空间
- **多语言界面**：14 种语言（中/英/德/日/韩/俄/法等），运行时切换
- **管理员自提权**：非管理员启动时自动请求 UAC 提权
- **上次备份记录**：界面底部显示最近一次成功备份的路径与时间

### 快速开始

1. 将 `SHBT` 文件夹放入你的西门子工程目录内
2. 以管理员身份运行 `SHBT.exe`
3. 工具会自动定位工程、识别类型
4. 勾选目标盘（可多选），点击「开始备份」

### 技术栈

- **运行时**：.NET Framework 4.8（Windows 7+ 内置）
- **编译**：.NET 10 SDK（`dotnet build -c Release`）
- **打包**：framework-dependent，单 exe ~5 KB，交付尺寸 ≈3 MB
- **依赖**：7-Zip 26.02 (LGPL) + ShadowSpawn (MIT)

### 开源许可

- [7-Zip](https://www.7-zip.org/) — LGPL
- [ShadowSpawn](https://github.com/candera/shadowspawn) — MIT

### 构建

```bash
git clone <repo>
cd SHBT
dotnet build SHBT.csproj -c Release
# 产物: bin/Release/net48/SHBT.exe
```

---

## English

### Overview

**SHBT (SIMATIC Hot Backup Tool)** is a hot-backup utility for Siemens automation projects. It leverages Windows Volume Shadow Copy (VSS) to snapshot project directories without downtime, file locks, or license risk, then archives them with 7-Zip to one or more target drives.

### Supported Project Types

| Type | Signature (directory OR file; either match qualifies) |
|------|-----------|
| **TIA Portal** | `XRef` directory OR `*.ap1x` file (e.g. `.ap15`/`.ap19`) |
| **STEP7 V5.X** | `AMOBJS` directory OR `*.s7p` project file |
| **WinCC Classic (v7.x/v8.x)** | `GRACS` / `ArchiveManager` directory OR `*.mcp` project file |

### Key Features

- **Zero-downtime backup** — VSS snapshots guarantee file consistency while the project remains live
- **Multi-target** — back up to several drives in a single run (check all that apply)
- **Flexible exclusions** — custom file/directory ignore patterns (`-xr!` passed to 7-Zip)
- **Drive safety** — license/dongle drives are detected, blocked by default, and require explicit confirmation
- **Pre-flight space check** — estimates source size and validates free space on each target before starting
- **14 UI languages** — Chinese, English, German, Japanese, Korean, Russian, French, and more; switch at runtime
- **Auto-elevation** — requests UAC elevation automatically when not launched as admin
- **Last-backup record** — displays the path and timestamp of the most recent successful backup

### Quick Start

1. Drop the `SHBT` folder into your Siemens project directory
2. Run `SHBT.exe` as Administrator
3. The tool auto-locates the project and detects its type
4. Check target drives (multi-select supported), then click "Start Backup"

### Tech Stack

- **Runtime**：.NET Framework 4.8 (built into Windows 7+)
- **SDK**：.NET 10 (`dotnet build -c Release`)
- **Packaging**：framework-dependent, ~5 KB exe, ~3 MB total deliverable
- **Dependencies**：7-Zip 26.02 (LGPL) + ShadowSpawn (MIT)

### Open Source Credits

- [7-Zip](https://www.7-zip.org/) — LGPL
- [ShadowSpawn](https://github.com/candera/shadowspawn) — MIT

### Build

```bash
git clone <repo>
cd SHBT
dotnet build SHBT.csproj -c Release
# Output: bin/Release/net48/SHBT.exe
```
