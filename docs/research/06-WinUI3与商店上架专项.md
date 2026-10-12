# WinUI 3 + Microsoft Store 路线风险排查

> 调研日期:2026-10-12。结论:路线可行。TreeView #9549 已在 WinAppSDK 1.6 修复;注册表写入走"full trust + allowElevation 预审"可解,备 Plan B。

## 1. TreeView 风险与解法

- microsoft-ui-xaml#9549:content 模式(ItemsSource)下容器回收时 TreeViewItem.IsExpanded 不保持,滚动后节点展开状态错乱。根因:TreeViewList.PrepareContainerForItemOverride 以容器而非 TreeViewNode 作为 IsExpanded 事实来源。
- 时间线:2024-04-15 报告 → 2024-07-23 closed-Fixed(milestone WinAppSDK 1.6)→ 2024-08-14 fix-released。WASDK 1.6(2024-09)稳定版起含修复,当前稳定版 2.5.1(2026-09-16)无此问题。
- 残留同族问题:#8054(IsSelected 不保持)、#8275(虚拟化下增删节点内存泄漏,2023)。解法:展开/选中状态由 ViewModel 持有,不依赖控件容器;增删子键后做节点重建与泄漏回归测试。

数万节点方案:

- TreeView 内部是扁平化的 TreeViewList(ListViewBase),自带 UI 虚拟化。
- 懒加载:节点设 HasUnrealizedChildren=true,Expanding 事件里枚举子键。regedit 同模型:HKCR/HKU 全量展开远超数万节点,任何时刻只物化可见区 + 已展开路径。
- 备选:Syncfusion SfTreeView(load-on-demand,社区许可免费);自研 ItemsRepeater + VirtualizingLayout(microsoft-ui-xaml#374 确认的官方自定义虚拟化途径)。

值列表(DataGrid 替代):

- WCT DataGrid 有 WinUI 3 版(7.1.2,NuGet: CommunityToolkit.WinUI.UI.Controls.DataGrid),已停止演进:WCT 8.x 未包含,官方文档归档,滚动性能差。
- 路线:① ListView + Grid 列头自绘(Toolkit 团队在 Labs-Windows #415 自述即用此法;单键值列表通常数千行内,ListView 虚拟化足够);② WASDK 2.5 实验版 TableView(2026-09-29 release notes),不首发依赖实验 API;③ Labs DataTable(0.1.x 预览);④ Syncfusion SfDataGrid(商业,社区许可免费)。

实证:

- Files 项目(商店上架、full trust)主列表是自研 BaseLayoutPage/DetailsLayoutPage/ColumnsLayoutPage 系列,基于原生 ListView/GridView 虚拟化与容器回收 + 异步图标加载;侧栏用 NavigationView 而非 TreeView,未用第三方 DataGrid。
- 微软 2026-05 公布 WinUI 3 Performance: A Leap Forward(microsoft-ui-xaml discussion #11096),以 Windows 11 文件资源管理器/记事本为基准:分配 -41%、瞬态分配 -63%、函数调用 -45%、启动耗时下降;成果随 WASDK 2.3.1+ 落地(样式延迟应用、ResourceDictionary 哈希加速、Icon 无 Grid 化)。

## 2. 权限模型(注册表写入 + 提权)

- Full trust:WinUI 3 桌面 MSIX 默认 mediumIL full trust,须声明 runFullTrust 受限能力。每个 WinUI 3 商店应用都带它,提交时在 Partner Center "Submission options" 写明用途。
- 读:无障碍。MSIX 注册表虚拟化只影响写入落点;full trust 进程可读真实注册表,HKLM\SYSTEM\CurrentControlSet 等从不虚拟化(WindowsAppSDK#6410,微软 Howard Kapustein 答复)。
- 写:非提权时,HKCU 写入被 COW 进包私有 hive(用户改了值,regedit 看不到);HKLM 写入即使成功也只对自己可见(#6410)。unvirtualizedResources 能关掉 HKCU 写虚拟化,官方定位是给微软及合作伙伴的桌面游戏,不适用。
- 关键结论:提权(elevated)进程运行时 MSIX 虚拟化整体禁用,写直达真实注册表。读写全部走提权实例,同时绕开虚拟化与 ACL。
- 提权模式(Stefan Wick 系列):exe 的 app.manifest 声明 requestedExecutionLevel(highestAvailable 或 requireAdministrator)+ 包清单声明 `<rescap:Capability Name="allowElevation"/>` + AppExecutionAlias 用 `Verb="runas"` 动态二次自启。

allowElevation 预审:

- 商店政策(2026-10 现行措辞):"subject to approval under strict criteria. If you intend to use this capability, contact reportapp@microsoft.com in advance with a detailed justification." 预审制,非自动拒绝;流程约 5 个工作日以上。
- 先例:Sysinternals Suite 在 Microsoft Store 上架,清单声明 runFullTrust + allowElevation + unvirtualizedResources 三件套。第三方 Universal Registry Editor 在店且明示只读("Currently the app only allows viewing the registry"),多数第三方未走 allowElevation 预审。

架构:

- 主方案:非提权主进程(浏览、搜索、只读)→ 首次写操作 UAC 提权拉起 elevated 二次实例(读写全通、无虚拟化),主/从实例经命名管道或 AppService 通信;提交前邮件 reportapp@microsoft.com 预审 allowElevation。
- Plan B(allowElevation 被拒):商店版只读浏览 + 写操作导出 .reg / 一键调起系统 regedit 定位到该项(ShellExecute runas 调起非打包 regedit.exe,不依赖 allowElevation);完整可写版经 winget/官网 MSIX 分发(sideload 声明受限能力的包无需审批)。
- 代价:商店版"一站式写入"降级为"引导式写入",影响约 30-40% 功能;发布渠道不受影响。

## 3. 商店审核风险

- Microsoft Store Policies v7.20(10.x 条款):更改 Windows 设置/首选项必须"使用受支持的方法并取得用户同意"。注册表编辑器是用户显式逐项操作,合规;应用描述如实说明用途与风险,提供备份/撤销。
- 认证时长通常数小时至 3 个工作日,含受限能力审批更久。
- 隐私:不收集/传输数据则声明"不收集数据";年龄分级选 Everyone;不声明用不到的能力(用户安装时会看到能力清单)。

## 4. WASDK 2.x 相关事实

- 当前稳定版 2.5.1(2026-09-16),版本全景见 07。
- 对本项目有用的能力:SystemBackdropElement(Mica/Acrylic 可放任意布局位置,2.0)、TitleBar 拖拽区自算(2.1.3)、2.3.1 启动/样式/资源查找性能优化、ApplicationData.GetForUnpackaged()(2.2.0,打包/非打包双发行)、2.5 实验 TableView。
- WASDK 1.6 起最低 .NET 8;2.x 可用 .NET 10 LTS(TFM 形如 net10.0-windows10.0.19041),社区已有 ".NET 10 SDK + WASDK 2.4" 发布先例(DeskBox)。
- OS 底线:Win10 1809+(核心 API);Mica/标题栏新特性需 Win11。

## 5. Store 上架工程要点

- 开发者账户注册免费:个人 2025-09 起、企业 2026-05 起,原 $19/$99 取消。
- MSIX:package identity(商店签名);声明且仅声明所需能力(runFullTrust [+ allowElevation]);uap10:TrustLevel="mediumIL" 默认即可;提交 .appxupload;Publisher 与账户一致。
- winget:上架商店后自动进入 msstore 源,无需额外操作;winget 社区源(winget-pkgs)是独立目录,需另行手动提交 manifest。

## 6. 判定表

| 风险点 | 判定 | 依据 |
|---|---|---|
| TreeView #9549 | 可控(已修复) | WASDK 1.6 修复并 fix-released,2.5.1 无此问题;残留 #8054/#8275 用"状态由 VM 持有"防御 |
| 数万节点树性能 | 可控 | 懒加载 + HasUnrealizedChildren + 原生虚拟化;备选 SfTreeView / ItemsRepeater |
| 值列表无官方 DataGrid | 可控(小妥协) | ListView+列头自绘是 Toolkit 官方做法;WCT DataGrid 停在 7.1.2;TableView 在实验期 |
| HKLM/HKCR 写入 | 可控(条件:写操作走提权实例) | 提权进程虚拟化禁用、写直达真实注册表;读无障碍 |
| allowElevation 过审 | 可控但需预审 + Plan B | 预审制(reportapp@microsoft.com);Sysinternals 三件套在店先例;被拒走"商店只读 + .reg/regedit 引导 + winget 完整版" |
| 商店审核(系统工具) | 可控 | 10.x 条款只要求受支持方法 + 用户同意;同类先例在店 |
| 开发者成本 | 无风险 | 注册免费、无年费 |

结论:WinUI 3 + 商店上架路线可行,两大历史顾虑均不构成否决项;唯一前置变量是 allowElevation 预审,排进提交前置流程并锁定 Plan B。

## 来源

- [microsoft-ui-xaml#9549](https://github.com/microsoft/microsoft-ui-xaml/issues/9549) / [#8054](https://github.com/microsoft/microsoft-ui-xaml/issues/8054) / [#8275](https://github.com/microsoft/microsoft-ui-xaml/issues/8275) / [#374](https://github.com/microsoft/microsoft-ui-xaml/issues/374) / [#11096 性能优化](https://github.com/microsoft/microsoft-ui-xaml/discussions/11096)
- [Syncfusion SfTreeView](https://www.syncfusion.com/winui-controls/treeview) / [ItemsRepeater](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/items-repeater) / [WCT DataGrid 归档](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/archive/windows/datagrid) / [Labs-Windows #415](https://github.com/CommunityToolkit/Labs-Windows/discussions/415) / [WASDK 2.0 release notes(TableView)](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0)
- [files-community/Files](https://github.com/files-community/Files) / [app-capability-declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations) / [WindowsAppSDK#6410](https://github.com/microsoft/WindowsAppSDK/issues/6410)
- [Stefan Wick: App Elevation Part 1](https://stefanwick.com/2018/10/01/app-elevation-samples-part-1/) / [Part 2](https://stefanwick.com/2018/10/06/app-elevation-samples-part-2/)
- [Sysinternals Suite(商店)](https://apps.microsoft.com/detail/9p7knl5rwt25) / [受限能力分析(deletethis.net)](https://deletethis.net/dave/2022-09/restricted-capabilities-and-msix-apis/) / [Universal Registry Editor(商店)](https://apps.microsoft.com/detail/9p44rxg1dfcf)
- [Microsoft Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies) / [App 认证流程](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-certification-process) / [开发者账户](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account) / [个人注册免费公告](https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/)
- [WASDK whats-new-for-developers](https://learn.microsoft.com/en-us/windows/apps/whats-new/whats-new-for-developers) / [DeskBox](https://github.com/Tianyu199509/DeskBox)
