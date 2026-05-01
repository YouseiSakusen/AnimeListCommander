using AnimeListCommander.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using R3;
using Wpf.Ui;
using Wpf.Ui.Controls;
using ZLogger;

namespace AnimeListCommander.Operations;

/// <summary>
/// アニメ情報の展開用 ViewModel です。
/// </summary>
public class AnimeExportViewModel : IDisposable
{
	private readonly ApplicationContext applicationContext;
	private readonly IServiceScopeFactory scopeFactory;
	private readonly ILogger<AnimeExportViewModel> logger;
	private readonly ISnackbarService snackbarService;
	private readonly BindableReactiveProperty<bool> isExecuting;
	private DisposableBag disposableBag;

	/// <summary>
	/// 選択可能なクール一覧を取得します。
	/// </summary>
	public IReadOnlyList<Season> Seasons { get; }

	/// <summary>
	/// 現在選択中のクールを取得または設定します。
	/// </summary>
	public BindableReactiveProperty<Season?> SelectedSeason { get; }

	/// <summary>
	/// 出力先パスを取得します。
	/// </summary>
	public BindableReactiveProperty<string> ExportPath { get; }

	/// <summary>
	/// 展開実行コマンドを取得します。
	/// </summary>
	public ReactiveCommand<Unit> ExportCommand { get; }

	/// <summary>
	/// クリッカブルマップ生成コマンドを取得します。
	/// </summary>
	public ReactiveCommand<Unit> GenerateMapCommand { get; }

	/// <summary>
	/// HTML紹介文並べ替えコマンドを取得します。
	/// </summary>
	public ReactiveCommand<Unit> SortHtmlCommand { get; }

	/// <summary>
	/// <see cref="AnimeExportViewModel"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="applicationContext">アプリケーションコンテキスト。</param>
	/// <param name="scopeFactory">スコープ生成ファクトリ。</param>
	/// <param name="logger">ロガー。</param>
	/// <param name="snackbarService">スナックバーサービス。</param>
	public AnimeExportViewModel(
		ApplicationContext applicationContext,
		IServiceScopeFactory scopeFactory,
		ILogger<AnimeExportViewModel> logger,
		ISnackbarService snackbarService)
	{
		this.applicationContext = applicationContext;
		this.scopeFactory = scopeFactory;
		this.logger = logger;
		this.snackbarService = snackbarService;

		this.Seasons = this.applicationContext.Seasons;

		var currentSeason = this.applicationContext.CurrentSeason;
		var matchedSeason = this.Seasons.FirstOrDefault(s =>
			s.Year == currentSeason?.Year && s.SeasonID == currentSeason?.SeasonID);
		this.SelectedSeason = new BindableReactiveProperty<Season?>(matchedSeason)
			.AddTo(ref this.disposableBag);

		var appConfig = this.applicationContext.AppConfiguration;
		var initialPath = matchedSeason is not null
			? appConfig.GetExportPath(matchedSeason)
			: appConfig.AnimeListRootPath;
		this.ExportPath = new BindableReactiveProperty<string>(initialPath)
			.AddTo(ref this.disposableBag);

		this.SelectedSeason
			.Select(s => s is not null ? appConfig.GetExportPath(s) : appConfig.AnimeListRootPath)
			.Subscribe(path => this.ExportPath.Value = path)
			.AddTo(ref this.disposableBag);

		this.isExecuting = new BindableReactiveProperty<bool>(false)
			.AddTo(ref this.disposableBag);

		this.ExportCommand = new ReactiveCommand<Unit>(async _ => await this.executeExportAsync())
			.AddTo(ref this.disposableBag);

		this.isExecuting
			.Select(x => !x)
			.Subscribe(canExec => this.ExportCommand.ChangeCanExecute(canExec))
			.AddTo(ref this.disposableBag);

		this.GenerateMapCommand = new ReactiveCommand<Unit>(async _ => await this.executeGenerateMapAsync())
			.AddTo(ref this.disposableBag);

		this.SortHtmlCommand = new ReactiveCommand<Unit>(async _ => await this.executeSortHtmlAsync())
			.AddTo(ref this.disposableBag);
	}

	/// <summary>
	/// 対象ディレクトリパスを返します。
	/// <see cref="SelectedSeason"/> が選択されている場合はそのクール用のパスを、
	/// 未選択の場合は <see cref="ExportPath"/> の値をそのまま返します。
	/// </summary>
	/// <returns>対象ディレクトリの絶対パス。</returns>
	private string getTargetDirectoryPath()
	{
		var season = this.SelectedSeason.Value;
		if (season is null)
			return this.ExportPath.Value;

		return this.applicationContext.AppConfiguration.GetExportPath(season);
	}

	/// <summary>
	/// クリッカブルマップ生成処理を実行します。
	/// </summary>
	private async ValueTask executeGenerateMapAsync()
	{
		var season = this.SelectedSeason.Value;
		if (season is null)
			return;

		this.logger.ZLogInformation($"[GenerateMap] 処理開始: {season.DisplayName}");
		await using var scope = this.scopeFactory.CreateAsyncScope();
		var operationService = scope.ServiceProvider.GetRequiredService<OperationService>();
		var result = await operationService.GenerateClickableMapAsync(season);

		switch (result)
		{
			case HalationGhostHtmlConvertResult.TargetFileNotFound:
				this.snackbarService.Show(
					"ファイル未検出",
					"clickableMap.txt が見つかりません。パスを確認してください。",
					ControlAppearance.Danger,
					null,
					TimeSpan.Zero);
				break;
			case HalationGhostHtmlConvertResult.OrderFileNotFound:
				this.snackbarService.Show(
					"ファイル未検出",
					"AnimeListOrder.txt が見つかりません。パスを確認してください。",
					ControlAppearance.Danger,
					null,
					TimeSpan.Zero);
				break;
			case HalationGhostHtmlConvertResult.AnimeWorkIdNotFound:
				this.snackbarService.Show(
					"作品ID未検出",
					"並び順ファイルに記載された作品IDがDBに存在しません。",
					ControlAppearance.Danger,
					null,
					TimeSpan.Zero);
				break;
			case HalationGhostHtmlConvertResult.Success:
				this.snackbarService.Show(
					"クリッカブルマップ生成完了",
					"ファイルを書き換えました。",
					ControlAppearance.Success,
					null,
					TimeSpan.FromSeconds(5));
				break;
		}
	}

	/// <summary>
	/// アニメ個別作品HTML並べ替え処理を実行します。
	/// </summary>
	private async ValueTask executeSortHtmlAsync()
	{
		var season = this.SelectedSeason.Value;
		if (season is null)
			return;

		this.logger.ZLogInformation($"[SortHtml] 処理開始: {season.DisplayName}");
		await using var scope = this.scopeFactory.CreateAsyncScope();
		var operationService = scope.ServiceProvider.GetRequiredService<OperationService>();
		var result = await operationService.SortHtmlByOrderAsync(season);

		switch (result)
		{
			case HalationGhostHtmlConvertResult.TargetFileNotFound:
				this.snackbarService.Show(
					"ファイル未検出",
					"animeListHtml.txt が見つかりません。パスを確認してください。",
					ControlAppearance.Danger,
					null,
					TimeSpan.Zero);
				break;
			case HalationGhostHtmlConvertResult.OrderFileNotFound:
				this.snackbarService.Show(
					"ファイル未検出",
					"AnimeListOrder.txt が見つかりません。パスを確認してください。",
					ControlAppearance.Danger,
					null,
					TimeSpan.Zero);
				break;
			case HalationGhostHtmlConvertResult.AnimeWorkIdNotFound:
				this.snackbarService.Show(
					"作品ID未検出",
					"並び順ファイルに記載された作品IDがHTMLに存在しません。",
					ControlAppearance.Danger,
					null,
					TimeSpan.Zero);
				break;
			case HalationGhostHtmlConvertResult.Success:
				this.snackbarService.Show(
					"並べ替え完了",
					"アニメ個別作品一覧の並び替えが完了しました。",
					ControlAppearance.Success,
					null,
					TimeSpan.FromSeconds(5));
				break;
		}
	}

	/// <summary>
	/// 展開処理を実行します。
	/// </summary>
	private async ValueTask executeExportAsync()
	{
		this.isExecuting.Value = true;
		try
		{
			var season = this.SelectedSeason.Value;
			if (season is null)
			{
				return;
			}

			this.logger.ZLogInformation($"[Deploy] 展開開始: {season.DisplayName}");
			await using var scope = this.scopeFactory.CreateAsyncScope();
			var operationService = scope.ServiceProvider.GetRequiredService<OperationService>();
			await operationService.DeployAsync(season, CancellationToken.None);
			this.logger.ZLogInformation($"[Deploy] 展開完了: {season.DisplayName}");
		}
		catch (Exception ex)
		{
			this.logger.ZLogError(ex, $"[Deploy] 展開中にエラーが発生しました: {ex.Message}");
		}
		finally
		{
			this.isExecuting.Value = false;
		}
	}

	/// <summary>
	/// リソースを解放します。
	/// </summary>
	public void Dispose()
	{
		this.disposableBag.Dispose();
	}
}
