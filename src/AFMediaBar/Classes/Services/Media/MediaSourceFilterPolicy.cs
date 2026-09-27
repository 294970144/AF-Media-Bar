using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>按应用来源执行既有允许列表判断，两条网易云通道共用允许状态。 / Applies the existing source allow-list, sharing NetEase permission across both channels.</summary>
public static class MediaSourceFilterPolicy
{
    public static bool IsAllowed(string? sourceId, SmtcSourceFilterSettings settings)
    {
        settings = settings.Normalize();
        if (!settings.Enabled)
            return true;
        if (string.IsNullOrWhiteSpace(sourceId))
            return false;
        var normalized = NetEaseSourcePolicy.NormalizeSourceId(sourceId.Trim());
        return settings.AllowedSourceIds!.Any(allowed => string.Equals(
            NetEaseSourcePolicy.NormalizeSourceId(allowed), normalized, StringComparison.OrdinalIgnoreCase));
    }
}
