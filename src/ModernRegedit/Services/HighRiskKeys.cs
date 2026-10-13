namespace ModernRegedit.Services;

public enum RiskLevel
{
    None,
    /// <summary>启动关键 / 身份验证：改错可致无法开机或登录。</summary>
    Critical,
    /// <summary>服务 / 自启动 / 劫持高发区：改错影响系统行为。</summary>
    Elevated,
}

public sealed record HighRiskArea(RiskLevel Level, string PathPrefix, string Reason, string Key);

/// <summary>高危键清单（research/05 §11）。按路径前缀匹配（忽略大小写）；Reason 为英文兜底，展示文案走 Risk_Reason_{Key} 资源。</summary>
public static class HighRiskKeys
{
    private static readonly HighRiskArea[] Areas =
    [
        new(RiskLevel.Critical, @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services",
            "Services/drivers: mistakes can break boot (0x7B) or disable services.", "Services"),
        new(RiskLevel.Critical, @"HKEY_LOCAL_MACHINE\SYSTEM\Select",
            "Boot control-set selection. Direct changes can make the system unbootable.", "Select"),
        new(RiskLevel.Critical, @"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001",
            "Boot control set. Direct structural changes can make the system unbootable.", "ControlSet001"),
        new(RiskLevel.Critical, @"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet002",
            "Boot control set. Direct structural changes can make the system unbootable.", "ControlSet002"),
        new(RiskLevel.Critical, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
            "Winlogon: wrong Userinit/Shell values can prevent login.", "Winlogon"),
        new(RiskLevel.Critical, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList",
            "Profile paths: wrong values can break user logon.", "ProfileList"),
        new(RiskLevel.Critical, @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager",
            "Session Manager: BootExecute / PendingFileRenameOperations affect boot.", "SessionManager"),
        new(RiskLevel.Elevated, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            "Autostart entries (Run / RunOnce).", "Run"),
        new(RiskLevel.Elevated, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            "Autostart entries (Run / RunOnce).", "RunOnce"),
        new(RiskLevel.Elevated, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
            "IFEO: a common malware hijack target.", "IFEO"),
        new(RiskLevel.Elevated, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            "User autostart entries (Run / RunOnce).", "UserRun"),
        new(RiskLevel.Elevated, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\RunOnce",
            "User autostart entries (Run / RunOnce).", "UserRunOnce"),
        new(RiskLevel.Elevated, @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\TimeZoneInformation",
            "Time zone data applies before logon.", "TimeZone"),
    ];

    /// <summary>返回命中的最高风险区域；未命中返回 null。</summary>
    public static HighRiskArea? Match(string keyPath)
    {
        HighRiskArea? best = null;
        foreach (var area in Areas)
        {
            if (keyPath.StartsWith(area.PathPrefix, StringComparison.OrdinalIgnoreCase)
                && (best is null || area.PathPrefix.Length > best.PathPrefix.Length))
            {
                best = area;
            }
        }

        return best;
    }
}
