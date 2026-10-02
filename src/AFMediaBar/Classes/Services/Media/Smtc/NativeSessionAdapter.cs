// Bridges raw WinRT SMTC sessions into the third-party wrapper type; owns every reflection detail in one place.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services.Media.Smtc;

/// <summary>
/// 把 WinRT 原生会话包装成第三方库的会话类型，使读路径可以在不依赖库事件链路的前提下取到媒体信息。
///
/// 第三方库把 <c>MediaSession</c> 的构造器设为 internal，因此只能用反射构造。本类型是该反射的唯一入口：
/// 一旦上游库改动构造签名，只需在此处跟进一次，其余代码不受影响。
/// Wraps raw WinRT sessions into the third-party session type so the read path can reach media information without depending on the
/// library's event chain.
///
/// The library marks <c>MediaSession</c>'s constructor internal, so instances can only be created reflectively. This type is the single
/// entry point for that reflection: if the upstream constructor signature changes, only this one place needs a fix.
/// </summary>
internal static class NativeSessionAdapter
{
    /// <summary>
    /// 库会话类型的缓存；类型查找与绑定每会话做一次即可，但缓存能让失败也只发生一次。
    /// Cached library session type; resolving and binding it once per session would be wasteful, and caching also confines a failure to one call.
    /// </summary>
    private static readonly Type? SessionType =
        typeof(MediaManager).GetNestedType("MediaSession", BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>
    /// 登记由本类型构造的会话，用来区分"库的字典持有"与"兜底路径持有"。
    ///
    /// 只有兜底构造的会话该被 AF 释放：库字典里的会话带着库自己的事件转发，重复释放会摘掉那些订阅。
    /// 用 <see cref="ConditionalWeakTable{TKey,TValue}"/> 而非集合，是因为会话被丢弃时登记会随之消失，不会留住对象。
    /// Tracks the sessions this type constructed so library-owned and fallback-owned ones can be told apart.
    ///
    /// Only fallback-constructed sessions may be released by AF: those in the library dictionary carry the library's own event
    /// forwarding, and releasing them again would detach those subscriptions. A <see cref="ConditionalWeakTable{TKey,TValue}"/> is used
    /// instead of a collection because the tracking entry disappears with the session, so no object is kept alive.
    /// </summary>
    private static readonly ConditionalWeakTable<MediaSession, FallbackOwnership> FallbackOwned = new();

    /// <summary>兜底会话的登记值，仅作标记。/ Marker value for a fallback session's tracking entry.</summary>
    private sealed class FallbackOwnership;

    /// <summary>包装一个 WinRT 原生会话；类型不可用、会话为 <see langword="null"/> 或构造失败时返回 <see langword="false"/>。
    /// Wraps one raw WinRT session, returning <see langword="false"/> when the type is unavailable, the session is
    /// <see langword="null"/>, or construction throws.</summary>
    /// <param name="controlSession">从 <c>WindowsSessionManager.GetSessions()</c> 取得的原生会话。/ A raw session obtained from <c>WindowsSessionManager.GetSessions()</c>.</param>
    /// <param name="manager">会话归属的管理器；库构造器需要它来登记事件转发目标。/ The owning manager; the library constructor needs it to wire event forwarding.</param>
    /// <param name="session">构造出的库会话，失败时为 <see langword="null"/>。/ The constructed library session, or <see langword="null"/> on failure.</param>
    public static bool TryWrap(
        GlobalSystemMediaTransportControlsSession? controlSession,
        MediaManager manager,
        out MediaSession? session)
    {
        session = null;
        if (controlSession is null || SessionType is null)
        {
            return false;
        }

        try
        {
            var constructor = SessionType.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                {
                    var parameters = candidate.GetParameters();
                    return parameters.Length == 2 &&
                           parameters[0].ParameterType == typeof(GlobalSystemMediaTransportControlsSession) &&
                           parameters[1].ParameterType == typeof(MediaManager);
                });

            if (constructor is null)
            {
                return false;
            }

            session = (MediaSession)constructor.Invoke([controlSession, manager]);
            if (session is null)
            {
                return false;
            }

            FallbackOwned.AddOrUpdate(session, new FallbackOwnership());
            return true;
        }
        catch (Exception exception) when (exception is TargetInvocationException or MemberAccessException
                                              or ArgumentException or InvalidCastException or NotSupportedException)
        {
            Debug.WriteLine($"[NativeSessionAdapter] Failed to wrap native session: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 该会话是否由 <see cref="TryWrap"/> 构造，因而该由 AF 释放。
    /// Whether the session was constructed by <see cref="TryWrap"/> and is therefore AF's to release.
    /// </summary>
    /// <param name="session">待判定的会话。/ The session to test.</param>
    public static bool IsFallbackOwned(MediaSession? session) =>
        session is not null && FallbackOwned.TryGetValue(session, out _);

    /// <summary>
    /// 释放由 <see cref="TryWrap"/> 构造的会话，解除它在原生会话上订阅的事件。
    /// 库的释放方法同样非公开，因此走反射；失败只记录日志，因为兜底路径此时已无会话可用。
    /// Releases a session produced by <see cref="TryWrap"/> and unsubscribes the events it attached to the native session. The library's
    /// disposal method is non-public as well, so it goes through reflection too; a failure is only logged because the fallback has no
    /// session left to serve at that point.
    /// </summary>
    /// <param name="session">待释放的会话。/ The session to release.</param>
    public static void Release(MediaSession? session)
    {
        if (session is null)
        {
            return;
        }

        FallbackOwned.Remove(session);

        try
        {
            SessionType?.GetMethod("Dispose", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(session, null);
        }
        catch (Exception exception) when (exception is TargetInvocationException or MemberAccessException
                                              or InvalidCastException or NotSupportedException)
        {
            Debug.WriteLine($"[NativeSessionAdapter] Failed to release native session: {exception.Message}");
        }
    }
}
