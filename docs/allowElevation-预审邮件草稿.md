# allowElevation 预审邮件

已发送（2026-10-13）。按 research/06 排期，结果预计 2026-10-20 前后。被拒预案见 PLAN.md §6（商店版只读 + winget/sideload 完整版）。

---

**Subject:** Pre-review request for the restricted capability "allowElevation" — ModernRegedit

Hello,

We are developing ModernRegedit, an open-source (MIT) registry editor for Windows built on Windows App SDK 2.5.1 / WinUI 3, and intend to declare the restricted capability `allowElevation` (together with `runFullTrust` and `unvirtualizedResources`) in our Microsoft Store package.

- App name: ModernRegedit
- Publisher: 华佳浩 (HuaJiahao), individual Partner Center account
- Package identity name: 4E406B62.ModernRegedit
- Package family name: 4E406B62.ModernRegedit_dkcj7grfjrc2g
- Store ID: 9N1Z8LLKBRC9
- Source code: https://github.com/danlingdan/ModernRegedit (private until first release)

Justification:

ModernRegedit lets users view and edit the Windows registry. Writing to protected areas (HKLM, HKCR, HKU) requires elevation. The app runs unelevated for browsing and searching; when the user performs a write action on a protected key, we launch an elevated secondary instance through the documented UAC flow, with registry virtualization disabled in that instance so writes reach the real registry. Elevation is user-initiated, per action, and preceded by an automatic backup of the target subtree.

We use the capability for no other purpose, and the app collects no data.

Please let us know if any further information is needed.

Thanks,
HuaJiahao
[你的联系邮箱]
