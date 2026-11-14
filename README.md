# ReplayMate

游戏启动时自动开启 AMD 即时重放（Instant Replay），退出后延迟关闭。WinUI 3 单窗口 + 托盘常驻，绿色免安装。

## 它怎么工作

AMD 官方从来没有提供"切换即时重放"的热键或命令行接口（只有"保存即时重放"的热键）。但 `AMDRSServ.exe` 订阅了注册表变化：

```
HKCU\SOFTWARE\AMD\DVR\InstantReplayEnabled   (DWORD)
```

写 `1` 约 0.5–3 秒内开始缓冲，写 `0` 立即停止，双向可逆、无需管理员权限、无需点击 UI。
ReplayMate 就是把这个写入接到"白名单游戏是否在运行"上。

## 快速开始

1. 双击 `ReplayMate.exe`（首次会弹引导，做一次 AMD 环境检查）
2. 「游戏」页 → **扫描 Steam / Epic** 勾选要管的游戏，或 **添加运行中的游戏**
3. 「设置」页确认三个时间参数，需要的话打开 **开机静默自启**
4. 启动游戏 → 等 `开启延迟` 秒后自动开录 → 退出游戏 → 等 `关闭延迟` 秒后自动关

> **前提**：AMD Software 必须在运行。它没有开机自启项，所以如果你不手动打开它（或自己设置自启），ReplayMate 只会提示"请先打开 AMD 软件"。程序首次引导会检查这一点。

## 匹配规则

| 规则 | 适用 | 优先级 |
|---|---|---|
| 完整路径 | 从"运行中进程"添加 | 最高 |
| 安装目录前缀 | Steam / Epic 库导入 | 高 |
| 进程名 | 手动添加、路径读不到时兜底 | 中 |
| 命令行关键词 | Minecraft 这类共用 `javaw.exe` 的游戏 | 与安装目录同级 |

命令行关键词只写在 `config.json` 里，界面上不暴露：

```json
{ "Name": "Minecraft", "CommandLineKeyword": "net.minecraft.client.main.Main" }
```

## 参数

| 参数 | 说明 |
|---|---|
| `--autostart` | 开机自启模式，启动后隐藏到托盘（自启快捷方式会自动带上） |
| `--dry-run` | 只记日志、不改注册表，用来验证识别是否准确 |
| `--verbose` | 每轮检测都写日志（进程数、匹配结果、状态机） |
| `--selftest` | 跑离线匹配自检，结果写到 `selftest-result.txt`，退出码 0=全过 |

## 文件位置

都在 exe 同目录（真正绿色）：

```
config.json          白名单与参数（自动生成）
logs\YYYYMMDD.log    运行日志，保留 48 小时，启动时清理
Assets\replaymate.ico 托盘图标
```

开机自启 = 在 `shell:startup` 放一个 `ReplayMate.lnk`，可在设置里开关。

## 启动方式（重要）

**请从文件资源管理器双击 `ReplayMate.exe`。**

不要从某些 IDE / 沙箱 / 下载器面板里"打开"它——那些宿主会把自己的低权限令牌传下去，进程完整性级别变成 Low，任务栏会拒绝托盘注册（日志里表现为 `LastError=5 拒绝访问`，开机自启也会写不进启动文件夹）。

程序对这种情况做了退化处理：托盘建不出来时，关闭窗口**直接退出程序**而不是躲进托盘，同时窗口顶部会显示一条可重试的警告。

## 已知限制

- **依赖 AMD Software 在运行**，且该注册表键属于未公开行为；AMD 驱动大版本升级后可能失效（失效表现为写入成功但编码引擎不动）
- 只做开关，不改缓冲时长（沿用你 AMD 里的设置）
- 关闭程序或暂停自动化时**保持现状**，不会替你还原即时重放状态
- 与手动操作的关系：只在"进入/退出游戏"这两个时刻写一次，中途你手动改不会被覆盖
- 若你同时用 Xbox Game Bar 的录制，AMD 官方说明二者不支持共存

## 构建

无需 Visual Studio，只需 .NET 8 SDK：

```powershell
dotnet build -c Debug -p:Platform=x64
dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish
```

WinUI 3 的 XAML 编译由 NuGet 包（`Microsoft.WindowsAppSDK`）提供，`dotnet build` 可直接完成。

自检（不依赖真实进程表，验证白名单匹配规则）：

```powershell
.\ReplayMate.exe --selftest     # 结果写入 selftest-result.txt，退出码 0 = 全过
```

## 免责声明

- 本项目与 **AMD 无任何关联**，未获 AMD 授权或背书。
- 程序依赖一个**未公开**的注册表值（`HKCU\SOFTWARE\AMD\DVR\InstantReplayEnabled`）。该行为没有官方文档或稳定性承诺，**AMD 驱动更新后可能随时失效**。
- 程序只做三件事：枚举进程、读写当前用户注册表、创建/删除启动项快捷方式。它**不注入游戏进程、不读写游戏内存、不模拟键盘鼠标输入**。
- 按现状（AS IS）提供，不附带任何明示或暗示担保；使用风险由使用者自行承担。

## 许可证

[MIT](LICENSE)。第三方组件许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
