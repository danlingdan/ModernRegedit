# ModernRegedit

现代化的 Windows 注册表编辑器——Fluent 设计、安全编辑、免费开源。

![Status](https://img.shields.io/badge/status-in%20development-orange) ![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D4) ![.NET](https://img.shields.io/badge/.NET-10.0-512BD4) ![License](https://img.shields.io/badge/license-MIT-blue)

🚧 开发中。English README: [English](README.md)。

## 为什么做

自带 regedit 搜索时界面冻结、没有撤销，界面停留在旧版 Windows；商业替代品收费且老旧，商店里的替代品只能只读。ModernRegedit 补上这个空位：现代界面、安全可写、免费开源。

## 计划功能

- 全类型键值的浏览与编辑
- 不阻塞界面的全库搜索 + 结果面板
- 修改、删除前自动备份，一键撤销
- .reg 导入前差异预览
- Ctrl+K 命令面板
- 标签页、地址栏、32/64 位视图、深色模式

## 路线图

| 里程碑 | 内容 |
|---|---|
| M0 | WinUI 3 工程脚手架、注册表服务 |
| M1 | 虚拟化树与值列表、地址栏 |
| M2 | 并行非阻塞搜索 |
| M3 | 编辑、.reg 导入导出与差异预览 |
| M4 | 自动备份、撤销、高危键警告 |
| M5 | 命令面板、设置页、Microsoft Store 发布 |

完整计划：[PLAN.md](PLAN.md) · 调研文档：[docs/research](docs/research)

## 构建

前置：Windows 11、[Visual Studio 2026](https://visualstudio.microsoft.com/) 18.10+（勾选".NET 桌面开发"与"Windows 应用开发"）、.NET 10 SDK。

```bash
git clone https://github.com/danlingdan/ModernRegedit.git
```

用 Visual Studio 打开解决方案按 F5。解决方案随 M0 里程碑落地。

## 参与贡献

欢迎贡献，见 [CONTRIBUTING.md](CONTRIBUTING.md)。参与即同意遵守[行为准则](.github/CODE_OF_CONDUCT.md)。

## 许可证

[MIT](LICENSE)
