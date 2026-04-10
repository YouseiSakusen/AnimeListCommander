using System.Reflection;
using System.Windows;
using AnimeListCommander.Intelligences;
using AnimeListCommander.Operations;
using Microsoft.Extensions.Logging;
using ObservableCollections;
using R3;
using Wpf.Ui;
using Wpf.Ui.Controls;
using ZLogger;

namespace AnimeListCommander;

/// <summary>
/// メインウィンドウの ViewModel です。
/// </summary>
public class MainWindowViewModel : IDisposable
{
    private readonly ILogger<MainWindowViewModel> logger;
    private readonly IThemeService themeService;
    private readonly INavigationService navigationService;
    private DisposableBag disposableBag;

    /// <summary>
    /// アプリケーションタイトルを表す読み取り専用リアクティブプロパティです。
    /// </summary>
    public ReadOnlyReactiveProperty<string> ApplicationTitle { get; }

    /// <summary>
    /// ナビゲーションメニューのアイテムコレクションです。
    /// </summary>
    public NotifyCollectionChangedSynchronizedViewList<NavigationViewItem> MenuItems { get; }

    /// <summary>
    /// <see cref="MainWindowViewModel"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="themeService">テーマサービス。</param>
    /// <param name="navigationService">ナビゲーションサービス。</param>
    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        IThemeService themeService,
        INavigationService navigationService)
    {
        this.logger = logger;
        this.themeService = themeService;
        this.navigationService = navigationService;

		this.logger.ZLogInformation($"MainWindowViewModel 初期化開始");

		var assemblyName = Assembly.GetEntryAssembly()?.GetName();
		var title = $"{assemblyName?.Name ?? "AnimeListCommander"} Ver.{assemblyName?.Version?.ToString() ?? "?"}";

		this.ApplicationTitle = Observable.Return(title)
			.ToReadOnlyReactiveProperty(title)
			.AddTo(ref this.disposableBag);

		this.MenuItems = new ObservableList<NavigationViewItem>
		{
			new NavigationViewItem
			{
				Content = "偵察",
				Icon = new SymbolIcon { Symbol = SymbolRegular.Radar20 },
				FontSize = 24,
				TargetPageType = typeof(AnimeScrapingPage),
			},
			new NavigationViewItem
			{
				Content = "展開",
				Icon = new SymbolIcon { Symbol = SymbolRegular.FolderArrowLeft24 },
				FontSize = 24,
				TargetPageType = typeof(AnimeExportPage),
			},
		}
		.ToNotifyCollectionChanged(SynchronizationContextCollectionEventDispatcher.Current)
		.AddTo(ref this.disposableBag);

		Application.Current.Dispatcher.BeginInvoke(() => this.navigationService.Navigate(typeof(AnimeScrapingPage)));
		this.logger.ZLogInformation($"MainWindowViewModel 初期化完了");
	}

	/// <summary>
	/// リソースを解放します。
	/// </summary>
	public void Dispose()
	{
		this.disposableBag.Dispose();
	}
}
