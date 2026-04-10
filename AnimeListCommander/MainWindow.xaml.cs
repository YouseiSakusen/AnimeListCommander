using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace AnimeListCommander;

/// <summary>
/// メインウィンドウのコードビハインドです。
/// </summary>
public partial class MainWindow : FluentWindow, INavigationWindow
{
	/// <summary>
	/// <see cref="MainWindow"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="viewModel">DI から注入される ViewModel。</param>
	/// <param name="navigationViewPageProvider">ページプロバイダーサービス。</param>
	/// <param name="navigationService">ナビゲーションサービス。</param>
	public MainWindow(
		MainWindowViewModel viewModel,
		INavigationViewPageProvider navigationViewPageProvider,
		INavigationService navigationService)
	{
		this.DataContext = viewModel;
		this.InitializeComponent();
		this.SetPageService(navigationViewPageProvider);
		navigationService.SetNavigationControl(this.RootNavigation);
	}

	/// <summary>
	/// ナビゲーションコントロールを返します。
	/// </summary>
	/// <returns>ウィンドウに配置された <see cref="INavigationView"/>。</returns>
	INavigationView INavigationWindow.GetNavigation() => this.RootNavigation;

	/// <summary>
	/// 指定されたページ型へナビゲートします。
	/// </summary>
	/// <param name="pageType">ナビゲート先のページ型。</param>
	/// <returns>ナビゲーションが成功した場合は <c>true</c>。</returns>
	bool INavigationWindow.Navigate(Type pageType) => this.RootNavigation.Navigate(pageType);

	/// <summary>
	/// DI サービスプロバイダーを設定します。
	/// </summary>
	/// <param name="serviceProvider">設定するサービスプロバイダー。</param>
	void INavigationWindow.SetServiceProvider(IServiceProvider serviceProvider) =>
		this.RootNavigation.SetServiceProvider(serviceProvider);

	/// <summary>
	/// ページプロバイダーサービスを設定します。
	/// </summary>
	/// <param name="navigationViewPageProvider">設定するページプロバイダー。</param>
	public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
		this.RootNavigation.SetPageProviderService(navigationViewPageProvider);

	/// <summary>
	/// ウィンドウを表示します。
	/// </summary>
	void INavigationWindow.ShowWindow() => this.Show();

	/// <summary>
	/// ウィンドウを閉じます。
	/// </summary>
	void INavigationWindow.CloseWindow() => this.Close();
}
