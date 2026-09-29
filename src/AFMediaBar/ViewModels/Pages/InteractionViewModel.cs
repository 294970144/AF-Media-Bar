using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>任务栏静置内容与托盘图标的点击、滚轮结果绑定。 / Click and wheel-result bindings for taskbar rest content and the tray icon.</summary>
public partial class InteractionViewModel : ObservableObject
{
    private readonly LocalizationService _localization;

    public PlayerClickAction ArtworkClickAction
    {
        get => Current.ArtworkClickAction;
        set => Update(Current with { ArtworkClickAction = value });
    }

    public PlayerClickAction TextClickAction
    {
        get => Current.TextClickAction;
        set => Update(Current with { TextClickAction = value });
    }

    public WheelAction PrimaryWheelAction
    {
        get => Current.PrimaryWheelAction;
        set => Update(Current with { PrimaryWheelAction = value });
    }

    public InteractionModifier Modifier
    {
        get => Current.Modifier;
        set => Update(Current with { Modifier = value });
    }

    public WheelAction ChordWheelAction
    {
        get => Current.ChordWheelAction;
        set => Update(Current with { ChordWheelAction = value });
    }

    public bool ShowWheelTooltips
    {
        get => Current.ShowWheelTooltips;
        set => Update(Current with { ShowWheelTooltips = value });
    }

    public TrayClickAction TrayClickAction
    {
        get => Current.TrayClickAction;
        set => Update(Current with { TrayClickAction = value });
    }

    public TrayWheelBehavior TrayPrimaryWheelAction
    {
        get => Current.TrayPrimaryWheelAction;
        set => Update(Current with { TrayPrimaryWheelAction = value });
    }

    public TrayWheelBehavior TrayChordWheelAction
    {
        get => Current.TrayChordWheelAction;
        set => Update(Current with { TrayChordWheelAction = value });
    }

    private static GlobalInteractionSettings Current => SettingsManager.Current.Interaction;

    /// <summary>
    /// 创建交互页视图模型，并订阅设置变更与界面语言变化。
    ///
    /// 本页的下拉框选项名由 XAML 的动态资源提供，切换语言时页面自己就会换字；订阅语言变化是为了让绑定与
    /// 这些设置派生属性在同一时机重新求值，避免留下半页旧文案。视图模型是单例，因此两个订阅都与进程同寿命。
    /// Creates the interaction view model and subscribes to settings changes and interface-language changes.
    ///
    /// The drop-down option names on this page come from XAML dynamic resources and follow a language change on their own;
    /// subscribing here makes the bindings re-evaluate on the same cue as these setting-derived properties, so no half of the
    /// page is left in the old text. The view model is a singleton, so both subscriptions live as long as the process.
    /// </summary>
    /// <param name="localization">界面语言服务：本页在它变化后刷新自己产出的文案。/ The interface-language service, whose change this page follows to refresh its own text.</param>
    public InteractionViewModel(LocalizationService localization)
    {
        _localization = localization;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private void Update(GlobalInteractionSettings settings)
    {
        SettingsManager.SetInteractionSettings(settings.Normalize());
        RaiseAll();
    }

    public void ResetInteraction() => SettingsManager.ResetInteraction();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.ResetScope is SettingsResetScope.Interaction or SettingsResetScope.All)
            RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(ArtworkClickAction));
        OnPropertyChanged(nameof(TextClickAction));
        OnPropertyChanged(nameof(PrimaryWheelAction));
        OnPropertyChanged(nameof(Modifier));
        OnPropertyChanged(nameof(ChordWheelAction));
        OnPropertyChanged(nameof(ShowWheelTooltips));
        OnPropertyChanged(nameof(TrayClickAction));
        OnPropertyChanged(nameof(TrayPrimaryWheelAction));
        OnPropertyChanged(nameof(TrayChordWheelAction));
    }
}
