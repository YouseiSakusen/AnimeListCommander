using AnimeListCommander.Contexts;
using Microsoft.Extensions.Logging;
using R3;
using ZLogger;

namespace AnimeListCommander.Operations;

/// <summary>
/// アニメ情報の展開用 ViewModel です。
/// </summary>
public class AnimeExportViewModel : IDisposable
{
	private readonly ApplicationContext applicationContext;
	private readonly OperationService operationService;
	private readonly ILogger<AnimeExportViewModel> logger;
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
	/// <param name="operationService">展開処理サービス。</param>
	/// <param name="logger">ロガー。</param>
	public AnimeExportViewModel(
		ApplicationContext applicationContext,
		OperationService operationService,
		ILogger<AnimeExportViewModel> logger)
	{
		this.applicationContext = applicationContext;
		this.operationService = operationService;
		this.logger = logger;

		this.Seasons = this.applicationContext.Seasons;

		var currentSeason = this.applicationContext.CurrentSeason;
		var matchedSeason = this.Seasons.FirstOrDefault(s =>
			s.Year == currentSeason?.Year && s.SeasonID == currentSeason?.SeasonID);
		this.SelectedSeason = new BindableReactiveProperty<Season?>(matchedSeason)
			.AddTo(ref this.disposableBag);

		var basePath = this.applicationContext.AppConfiguration.AnimeListRootPath;
		var initialPath = matchedSeason is not null ? $@"{basePath}\{matchedSeason.Year}-{(int)matchedSeason.SeasonID}" : basePath;
		this.ExportPath = new BindableReactiveProperty<string>(initialPath)
			.AddTo(ref this.disposableBag);

		this.SelectedSeason
			.Select(s => s is not null ? $@"{basePath}\{s.Year}-{(int)s.SeasonID}" : basePath)
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

		this.GenerateMapCommand = new ReactiveCommand<Unit>(_ => this.logger.ZLogInformation($"TODO: GenerateMapCommand は未実装です。"))
			.AddTo(ref this.disposableBag);

		this.SortHtmlCommand = new ReactiveCommand<Unit>(_ => this.logger.ZLogInformation($"TODO: SortHtmlCommand は未実装です。"))
			.AddTo(ref this.disposableBag);
	}

	private async Task executeExportAsync()
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
			await this.operationService.DeployAsync(season, CancellationToken.None);
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
