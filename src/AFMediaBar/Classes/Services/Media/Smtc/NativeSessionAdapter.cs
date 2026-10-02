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

    /// <summary>
    /// 库的构造器在原生会话上订阅的三个事件，及其对应处理方法名。
    ///
    /// 必须自己退订的原因是库的 <c>Dispose</c> 对兜底实例是空转：它先走
    /// <c>MediaManager.RemoveSource</c>，而后者要求会话在 <c>CurrentMediaSessions</c> 里才会继续，
    /// 兜底构造的实例按设计就不在字典里，因此整段退订与置空都不会执行。
    /// The three events the library's constructor subscribes on the native session, with the handler method each one maps to.
    ///
    /// Unsubscribing has to be done here because the library's <c>Dispose</c> is a no-op for a fallback instance: it goes through
    /// <c>MediaManager.RemoveSource</c> first, which only continues when the session is in <c>CurrentMediaSessions</c>, and a
    /// fallback-constructed instance is deliberately absent from that dictionary, so neither the unsubscription nor the nulling runs.
    /// </summary>
    private static readonly (string EventName, string HandlerName)[] SubscribedEvents =
    [
        (nameof(GlobalSystemMediaTransportControlsSession.MediaPropertiesChanged), "OnSongChangeAsync"),
        (nameof(GlobalSystemMediaTransportControlsSession.PlaybackInfoChanged), "OnPlaybackInfoChanged"),
        (nameof(GlobalSystemMediaTransportControlsSession.TimelinePropertiesChanged), "OnTimelinePropertiesChanged")
    ];

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
    /// 释放由 <see cref="TryWrap"/> 构造的会话，并把 <c>ControlSession</c> 置空，使任何残留引用立刻被
    /// <see cref="MediaSessionGuard.IsUsable"/> 判为不可用。
    ///
    /// 走反射而非库的 <c>Dispose</c>：那条路径对不在字典里的会话整段空转（见 <see cref="SubscribedEvents"/>），
    /// 而构造器订阅的三个事件必须真正摘掉，否则原生会话会一直持有这些包装实例，每一轮兜底读都多留一批。
    /// 失败只记录日志：走到这一步时兜底路径已经没有可用会话，让异常逃出去只会把读路径打断。
    /// Releases a session produced by <see cref="TryWrap"/> and nulls its <c>ControlSession</c> so any leftover reference is immediately
    /// judged unusable by <see cref="MediaSessionGuard.IsUsable"/>.
    ///
    /// This goes through reflection instead of the library's <c>Dispose</c>: that path is a no-op for a session outside the dictionary
    /// (see <see cref="SubscribedEvents"/>), yet the three events the constructor subscribed must genuinely come off, or the native session
    /// keeps holding these wrappers and every fallback round leaves another batch behind. Failures are only logged, because by this point
    /// the fallback has no session left to serve and letting the exception out would only break the read path.
    /// </summary>
    /// <param name="session">待释放的会话。/ The session to release.</param>
    public static void Release(MediaSession? session)
    {
        if (session is null)
        {
            return;
        }

        FallbackOwned.Remove(session);

        // 先取出来再置空：ControlSession 一旦为 null 就再也拿不到原生会话，而退订必须对着原生会话做。
        // Read it before nulling: once ControlSession is null the native session is gone, and unsubscribing has to happen against it.
        var controlSession = session.ControlSession;
        if (controlSession is not null)
        {
            foreach (var (eventName, handlerName) in SubscribedEvents)
            {
                Unsubscribe(controlSession, session, eventName, handlerName);
            }

            NullOutControlSession(session);
        }
    }

    /// <summary>
    /// 摘掉一个事件：把库的处理方法作为委托重新绑定到这个实例，再交给 WinRT 事件的 <c>-</c> 访问器。
    /// 与 <c>+=</c> 订阅时构造的是同一个目标与同一个方法，因此 WinRT 侧的委托比较能够匹配上并真正移除。
    /// Detaches one event: the library's handler method is rebound to this instance as a delegate and handed to the WinRT event's
    /// <c>-</c> accessor. This is the same target and the same method the <c>+=</c> subscription used, so the delegate comparison on the
    /// WinRT side matches and the handler really comes off.
    /// </summary>
    private static void Unsubscribe(
        GlobalSystemMediaTransportControlsSession controlSession,
        MediaSession session,
        string eventName,
        string handlerName)
    {
        try
        {
            var eventInfo = controlSession.GetType().GetEvent(eventName);
            var removeAccessor = eventInfo?.GetRemoveMethod(true);
            var handlerMethod = SessionType?.GetMethod(
                handlerName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (removeAccessor is null || handlerMethod is null)
            {
                return;
            }

            var delegateType = removeAccessor.GetParameters()[1].ParameterType;
            removeAccessor.Invoke(controlSession, [Delegate.CreateDelegate(delegateType, session, handlerMethod)]);
        }
        catch (Exception exception) when (exception is TargetInvocationException or MemberAccessException
                                              or ArgumentException or InvalidCastException or NotSupportedException)
        {
            Debug.WriteLine($"[NativeSessionAdapter] Failed to detach {eventName}: {exception.Message}");
        }
    }

    /// <summary>
    /// 把 <c>ControlSession</c> 置空，与库关闭会话后的状态保持一致。
    /// 这样即便有引用在本轮释放之后才被读取，也会立刻被有效性检查挡下，而不是读到已退订的原生会话。
    /// Nulls <c>ControlSession</c> to match the state the library leaves a closed session in, so a reference read after this release is
    /// turned away by the validity check instead of touching a session whose events are already detached.
    /// </summary>
    private static void NullOutControlSession(MediaSession session)
    {
        try
        {
            SessionType?.GetProperty(
                    nameof(MediaManager.MediaSession.ControlSession),
                    BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(session, null);
        }
        catch (Exception exception) when (exception is TargetInvocationException or MemberAccessException
                                              or ArgumentException or InvalidCastException or NotSupportedException)
        {
            Debug.WriteLine($"[NativeSessionAdapter] Failed to clear ControlSession: {exception.Message}");
        }
    }
}
