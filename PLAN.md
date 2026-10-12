# ModernRegedit 计划

现代化 Windows 注册表编辑器：Fluent 风格、完整可写、开源（MIT），主分发渠道 Microsoft Store。

- 技术方案见本文 §3；开发机与依赖版本见 [docs/开发环境.md](docs/开发环境.md)；调研数据见 docs/research/。
- 定稿：2026-10-12。

## 1. 定位

**商店里第一个「现代界面 + 完整可写 + 免费」的注册表编辑器。**

市场事实（research/01、06）：

- 商业产品 Registry Workshop（$29.95）、Registrar Pro（$47.95+）、jv16（$69）功能全，界面停留在 Win32 时代。
- 开源没有完整对标：TotalRegistry（1645★）界面老旧；0x5bfa/RegistryEditor（146★）功能残缺。
- Win11 自带 regedit 无深色模式、无标签页，搜索时界面冻结；微软表示 regedit 现代化"在计划中，无时间表"（2026-03）。
- Microsoft Store 第三方注册表编辑器只有只读产品。

差异化三件套：

1. 修改、删除前自动备份，一键撤销
2. .reg 导入前差异预览
3. Ctrl+K 命令面板

目标人群：系统管理员、开发者、安全研究人员、装机调优玩家。

## 2. 痛点与对策

| 痛点 | regedit 现状 | 对策 |
|---|---|---|
| 搜索卡死 | Ctrl+F 同步遍历，界面冻结 | 分片并行搜索 + 流式结果面板 |
| 无撤销 | 误删无法恢复，惯例是"先手动备份" | 自动备份 + 一键撤销 |
| 定位慢 | 跳转、路径输入、位置记忆弱 | 地址栏直达、收藏夹、位置记忆 |
| 值编辑弱 | REG_MULTI_SZ 分隔符混乱、二进制编辑简陋 | 全类型编辑对话框 |

（用户证据与 Kano 分类见 research/02。）

## 3. 技术方案

| 层 | 选型 |
|---|---|
| 语言 | C# / .NET 10 LTS |
| 界面 | WinUI 3 / Windows App SDK 2.5.1 |
| 架构 | MVVM / CommunityToolkit.Mvvm 8.4.2 |
| Win32 调用 | 自写 P/Invoke 薄层（advapi32，DllImport） |
| 打包 | MSIX：runFullTrust + unvirtualizedResources + allowElevation，从第一天就是 packaged |
| 分发 | Microsoft Store（自动进 winget msstore 源）+ GitHub Releases |

选型依据（research/03、06）：TreeView 缺陷 #9549 已随 WASDK 1.6 修复；微软 2026-05 启动 WinUI 3 性能跃升计划；Files（45.9k★）证明商店级应用可行。Avalonia 因 TreeView 大数据性能差排除，WPF 曾为主推方案、因 Store 分发改选 WinUI 3。

关键设计：

- 键树：懒加载（`HasUnrealizedChildren` + Expanding 按需枚举）；展开、选中状态存 ViewModel（规避 #8054、#8275）。
- 值列表：ListView 自绘列头（CommunityToolkit DataGrid 已停维护）。
- 搜索：按根键分片并行，结果流式写入面板，可取消；输入防抖 250ms。
- 缓存：LRU + `RegNotifyChangeKeyValue` 失效；键删除后重注册通知。
- 32/64 位：所有调用显式传 `KEY_WOW64_32KEY/64KEY`；界面显示当前视图；HKCR 显示真实写入位置（写入落 HKCU）。
- 撤销：修改前导出子树快照；不用 KTM 事务（存在非原子性缺陷）。
- .reg：UTF-16LE + BOM；`[-HKEY...]` 删键；hex(2)/hex(7) 前缀（规范见 research/05）。
- 监控（v2）：单键用 `RegNotifyChangeKeyValue`；全局活动流用 ETW Kernel-Registry。

权限模型（research/06）：

- 读取不需要提权。
- 写入走 UAC 提权实例：提权进程关闭注册表虚拟化，写直达真实注册表；非提权进程写 HKCU 会进包私有 hive。
- `allowElevation` 是受限能力，需邮件预审（reportapp@microsoft.com，排期约 5 个工作日），M0 提交。先例：商店版 Sysinternals Suite 携带同组能力。
- 预审被拒：商店版只读 + 引导写入；完整版走 winget/sideload。

## 4. 界面

- Fluent 2：Mica 主窗口；Acrylic 用于命令面板、菜单浮层；窗口圆角 8px、控件 4px；Segoe UI Variable；亮暗双主题。
- 布局：TabView 标签页 → 工具栏（前进/后退/上级 + 可编辑面包屑地址栏 + 搜索）→ 左键树、右值列表 → 状态栏（项数、视图位数、位置记忆）。
- 值类型：图标 + 颜色 + 文字三重编码；行高 32px（紧凑 28px）；长数据截断 + tooltip。
- 安全交互：高危键（SERVICES、Winlogon、IFEO 等）分级警告条；删除红色二次确认；写入操作显示提权标识。
- 无障碍：键盘全可达、F6 分区焦点、对比度 ≥4.5:1、UIA、resw 多语言（zh-CN / en）。

（布局线框图与设计 token 见 research/04。）

## 5. 路线图

| 里程碑 | 内容 | 出口标准 |
|---|---|---|
| M0 脚手架 ✅（2026-10-13） | WinUI 3 packaged 工程、三能力清单、三栏空壳、RegistryService、懒加载枚举；提交 allowElevation 预审 | 浏览五大根键 |
| M1 浏览（第 2–3 周） | 虚拟化树与值列表、地址栏跳转、前进后退、32/64 切换、亮暗主题、类型图标 | 浏览流畅 |
| M2 搜索（第 3–4 周） | 并行搜索、范围勾选、流式结果面板、双击跳转、取消 | 全库搜索不冻结界面 |
| M3 编辑与 .reg（第 5–6 周） | 键值增删改、全类型编辑对话框、提权写入、.reg 导入导出、导入差异预览 | .reg 双向兼容 |
| M4 安全（第 7–8 周） | 自动备份、撤销重做、高危键警告、收藏夹、位置记忆 | 误操作可恢复 |
| M5 发布（第 9–10 周） | Ctrl+K、设置页、多语言、无障碍、商店素材、Store 提交、GitHub Releases | v1.0 上架 |

- v1.1：快照对比、正则搜索替换、ACL 编辑器、`regedit://` 协议、最后写入时间。
- v2.0：实时监控（ETW）、离线 hive 挂载、审计时间线、远程注册表、多机管理。

## 6. 风险与对策

| 风险 | 对策 |
|---|---|
| allowElevation 预审被拒 | 商店版只读 + 引导写入；完整版走 winget/sideload |
| TreeView 残余缺陷（#8054、#8275） | 状态存 ViewModel；关注 TableView 进展 |
| 全库搜索性能 | 分片并行 + 流式首屏 + 可取消 |
| 误操作损坏系统 | 自动备份是 M4 出口标准；只读模式；高危键分级警告 |
| 32/64 位视图混淆 | P/Invoke 层统一传标志；界面显示当前视图 |
| 微软官方现代化 regedit | 差异化押注备份、diff、审计、离线挂载、中文生态 |
| GitHub 版信任 | 商店版由微软签名；GitHub 版用户量起来后购 OV 证书 |

## 7. 文档索引

| 文档 | 内容 |
|---|---|
| [docs/开发环境.md](docs/开发环境.md) | 本机环境实测、依赖版本决策 |
| [docs/research/01-竞品分析.md](docs/research/01-竞品分析.md) | 竞品功能、定价、市场空白 |
| [docs/research/02-产品功能与用户痛点.md](docs/research/02-产品功能与用户痛点.md) | 用户画像、痛点证据、功能分档 |
| [docs/research/03-技术栈选型.md](docs/research/03-技术栈选型.md) | 桌面框架对比数据 |
| [docs/research/04-UI设计方向.md](docs/research/04-UI设计方向.md) | Fluent 规范、线框图、设计 token |
| [docs/research/05-注册表技术基础.md](docs/research/05-注册表技术基础.md) | 注册表架构、值类型、.reg 规范、高危键 |
| [docs/research/06-WinUI3与商店上架专项.md](docs/research/06-WinUI3与商店上架专项.md) | TreeView 现状、MSIX 权限、allowElevation 预审 |
| [docs/research/07-工具链最新稳定版本.md](docs/research/07-工具链最新稳定版本.md) | 各工具链版本核实记录 |
