using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现静置层组件的开关和参数，状态由共享的设置视图模型持有。</summary>
public partial class ComponentsSettingsPage : INavigableView<ExtraFeaturesViewModel>
{
    /// <summary>与媒体和通知页共用的设置状态。</summary>
    public ExtraFeaturesViewModel ViewModel { get; }

    /// <summary>创建组件设置页。</summary>
    public ComponentsSettingsPage(ExtraFeaturesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) =>
        SettingsRevealAnimator.Play(sender as Panel);

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.Components"))
            ViewModel.ResetComponents();
    }
}
