# allowElevation 预审邮件草稿

M0 唯一剩余事项。收件人：`reportapp@microsoft.com`，发出后约 5 个工作日出结果（见 research/06）。
发送前补两处：Partner Center 注册后填账户信息；首次商店提交前 Package Identity 可暂缺。

---

**Subject:** Pre-review request for the restricted capability "allowElevation" — ModernRegedit

Hello,

We are developing ModernRegedit, an open-source (MIT) registry editor for Windows built on Windows App SDK 2.5.1 / WinUI 3, and intend to declare the restricted capability `allowElevation` (together with `runFullTrust` and `unvirtualizedResources`) in our Microsoft Store package.

- App name: ModernRegedit
- Publisher: HuaJiahao (individual developer)
- Partner Center account: [注册后填写：账户邮箱 / Seller ID]
- Package identity: [首次提交后填写；可暂缺]
- Source code: https://github.com/danlingdan/ModernRegedit (private until first release)

Justification:

ModernRegedit lets users view and edit the Windows registry. Writing to protected areas (HKLM, HKCR, HKU) requires elevation. The app runs unelevated for browsing and searching; when the user performs a write action on a protected key, we launch an elevated secondary instance through the documented UAC flow, with registry virtualization disabled in that instance so writes reach the real registry. Elevation is user-initiated, per action, and preceded by an automatic backup of the target subtree.

We use the capability for no other purpose, and the app collects no data.

Please let us know if any further information is needed.

Thanks,
HuaJiahao
[你的联系邮箱]
