using System.Diagnostics;
using System.Text;
using AnimeListCommander.Contexts;
using R3;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// アニメ情報の偵察（収集）用 ViewModel です。
/// </summary>
public class AnimeScrapingViewModel : IDisposable
{
	private readonly ApplicationContext applicationContext;
	private readonly IntelligenceService intelligenceService;
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
	/// ログ表示用テキストを取得または設定します。
	/// </summary>
	public BindableReactiveProperty<string> LogText { get; }

	/// <summary>
	/// 最新情報偵察コマンドを取得します。実行中はボタンが無効化されます。
	/// </summary>
	public ReactiveCommand<Unit> ScrapingCommand { get; }

	/// <summary>
	/// <see cref="AnimeScrapingViewModel"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="applicationContext">アプリケーションコンテキスト。</param>
	/// <param name="intelligenceService">アニメ情報収集サービス。</param>
	public AnimeScrapingViewModel(ApplicationContext applicationContext, IntelligenceService intelligenceService)
	{
		this.applicationContext = applicationContext;
		this.intelligenceService = intelligenceService;

		this.Seasons = this.applicationContext.Seasons;

		var currentSeason = this.applicationContext.CurrentSeason;
		var matchedSeason = this.Seasons.FirstOrDefault(s =>
			s.Year == currentSeason?.Year && s.SeasonID == currentSeason?.SeasonID);
		this.SelectedSeason = new BindableReactiveProperty<Season?>(matchedSeason)
			.AddTo(ref this.disposableBag);

		this.LogText = new BindableReactiveProperty<string>(string.Empty)
			.AddTo(ref this.disposableBag);

		// 実行中フラグ。CanExecute の制御に使用します。
		this.isExecuting = new BindableReactiveProperty<bool>(false)
			.AddTo(ref this.disposableBag);

		this.ScrapingCommand = new ReactiveCommand<Unit>(async _ => await this.executeScrapingAsync())
			.AddTo(ref this.disposableBag);

		// isExecuting の変化を CanExecute に反映します。
		this.isExecuting
			.Select(x => !x)
			.Subscribe(canExec => this.ScrapingCommand.ChangeCanExecute(canExec))
			.AddTo(ref this.disposableBag);
	}

	/// <summary>
	/// 偵察コマンドの非同期実行処理です。
	/// コンテキストにクールを確定させた後、IntelligenceService でアニメ情報を収集します。
	/// </summary>
	private async Task executeScrapingAsync()
	{
		this.isExecuting.Value = true;
		var sb = new StringBuilder(this.LogText.Value);
		try
		{
			var season = this.SelectedSeason.Value;
			if (season is null) return;

			sb.AppendLine($"[開始] {season.DisplayName} の偵察を開始します...");
			this.LogText.Value = sb.ToString();

			this.applicationContext.CurrentSeason = season;
			var targets = this.applicationContext.GetCurrentSeasonTargets();
			var reportPath = await this.intelligenceService.CrawlAllAsync(targets.ToList(), season);

			var results = this.intelligenceService.LastSaveResults;
			sb.AppendLine($"[完了] {results.Count} 件のアニメ作品を処理しました。（New={results.Count(r => r.Status == SaveStatus.New)}, Updated={results.Count(r => r.Status == SaveStatus.Updated)}, Skipped={results.Count(r => r.Status == SaveStatus.Skipped)}, Failed={results.Count(r => r.Status == SaveStatus.Failed)}）");
			sb.AppendLine($"レポート: {reportPath}");
			this.LogText.Value = sb.ToString();

			Process.Start(new ProcessStartInfo { FileName = reportPath, UseShellExecute = true });
		}
		catch (Exception ex)
		{
			sb.AppendLine($"[エラー] {ex.Message}");
			this.LogText.Value = sb.ToString();
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
