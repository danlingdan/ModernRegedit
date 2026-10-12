# AGENTS.md

AI 编码代理在本仓库工作的规则与入口。本文件只放硬性规则和索引，细节一律引用 docs/ 下的专项文档；改动流程时同步更新对应文档。

## 项目是什么

ModernRegedit——现代化的 Windows 注册表编辑器。WinUI 3 / Windows App SDK 2.5.1 + .NET 10 LTS，MSIX packaged，主分发渠道 Microsoft Store，MIT 开源。

- 权威计划与路线图：[PLAN.md](PLAN.md)（里程碑 M0–M5、技术决策、风险对策）。动手前先读它的对应里程碑。
- 解决方案文件是 **`ModernRegedit.slnx`**（不要创建 .sln）。

## 硬性规则

1. **一个功能一个提交**，禁止大批量混合提交。提交信息用 Conventional Commits（`feat:` / `fix:` / `docs:` / `chore:` / `ci:`）。
2. **只用最新稳定版**工具链与依赖（非 preview / RC / alpha / insider）。当前钉版见 [docs/开发环境.md](docs/开发环境.md) 的"项目依赖版本"节。
3. **每个功能提交前必须本地构建通过**；UI 功能必须部署到本机验证交互后才能提交（方法见 [docs/开发工作流.md](docs/开发工作流.md)）。
4. **文档风格**：精炼短句、表格优先；不写防御性描述；不用非必要术语；硬事实与来源链接必须保留。
5. `docs/research/` 是调研存档，不修改历史结论；新调研追加新编号文件，并在 PLAN.md §7 索引登记。

## 常用命令

```bash
# 构建（注意：不要对 slnx 传 /p:Platform=x64，它没有 x64 解决方案配置，会报 MSB4126）
dotnet build src/ModernRegedit/ModernRegedit.csproj -c Debug -p:Platform=x64

# 部署到本机（开发者模式）+ 启动
# 命令细节与启动路径见 docs/开发工作流.md

# 日常开发主循环：VS 2026 打开 ModernRegedit.slnx 按 F5
```

## 文档地图

| 文档 | 内容 |
|---|---|
| [PLAN.md](PLAN.md) | 产品定位、技术决策、里程碑与风险 |
| [docs/开发环境.md](docs/开发环境.md) | 本机环境实测、依赖钉版、打包命令 |
| [docs/开发工作流.md](docs/开发工作流.md) | 功能开发循环、部署、UIA 验证、事件日志排查 |
| [docs/商店发布信息.md](docs/商店发布信息.md) | Partner Center 身份、PFN、Store ID |
| [docs/allowElevation-预审邮件草稿.md](docs/allowElevation-预审邮件草稿.md) | 待用户发送的受限能力预审邮件 |
| [docs/research/](docs/research/) | 调研存档（竞品、功能、技术栈、UI、注册表底层、上架、工具链、开源规范、仓库工程） |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 外部贡献者流程（分支、提交、PR 规范） |

## 已知坑（一条一句话，细节在各文档）

- slnx 没有解决方案级 x64 配置：构建传平台直接构建 csproj。
- `Microsoft.Win32.Registry` 不支持变更通知与事务：统一走自写 DllImport 层 `RegistryNative`（CsWin32 生成器在本机不可用）。
- 枚举大键（如 SOFTWARE 约 2800 子键）必须在后台线程，结果经 `DispatcherQueue` 回 UI；子键不做逐个计数查询（N+1 会挂死 UI 线程）。
- WinUI `TreeViewItem` 的 UIA Name 是 CLR 类型名，真实标签在内层 Text；视口外节点未物化，UIA 查找需先 `ScrollIntoView`。
- TFM 用 `net10.0-windows10.0.26100`（28000 的 SDK 投影未发布）。
- 改包身份须与 [docs/商店发布信息.md](docs/商店发布信息.md) 同步。

## 当前状态与里程碑纪律

- 当前：M0 完成（2026-10-13），下一里程碑见 PLAN.md §5。
- 里程碑完成时必须：PLAN.md 表格打勾 + CHANGELOG.md 追加条目 + push 后确认 CI 绿（`gh run watch`）。
