using System.Text.RegularExpressions;
using AnimeListCommander.Contexts;
using AnimeListCommander.Helpers;
using AnimeListCommander.Intelligences.Translators;
using AnimeListCommander.Masters;
using HalationGhost.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// 偵察対象リストを巡回してアニメ情報を収集するサービスです。
/// </summary>
public class IntelligenceService
{
	private readonly ILogger<IntelligenceService> logger;
	private readonly IServiceScopeFactory scopeFactory;

	/// <summary>
	/// 直前の保存処理結果一覧を取得します。
	/// </summary>
	public IReadOnlyList<SaveResult> LastSaveResults { get; private set; } = Array.Empty<SaveResult>();

	/// <summary>
	/// <see cref="IntelligenceService"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="logger">ロガー。</param>
	/// <param name="scopeFactory">スコープファクトリー。</param>
	public IntelligenceService(ILogger<IntelligenceService> logger, IServiceScopeFactory scopeFactory)
	{
		this.logger = logger;
		this.scopeFactory = scopeFactory;
	}

	/// <summary>
	/// プライマリサイトでベースリストを構築し、セカンダリサイトおよび Annict で順次補完して DB へ保存します。
	/// </summary>
	/// <param name="targets">偵察対象リスト。</param>
	/// <param name="season">取得対象のクール。Annict 補完に使用します。</param>
	/// <param name="ct">キャンセルトークン。</param>
	public async ValueTask<string> CrawlAllAsync(List<ScrapingTarget> targets, Season season, CancellationToken ct = default)
	{
		using var scope = this.scopeFactory.CreateScope();
		var sp = scope.ServiceProvider;

		var annictService = sp.GetRequiredService<AnnictService>();
		var officialPageTitleService = sp.GetRequiredService<OfficialPageTitleService>();
		var repository = sp.GetRequiredService<IntelligenceRepository>();
		var masterRepository = sp.GetRequiredService<MasterRepository>();

		var primaryTarget = targets.Single(t => t.IsPrimaryInput);
		var secondaryTargets = targets.Where(t => !t.IsPrimaryInput).ToList();

		var masterList = await this.scrapeAndTranslatePrimaryAsync(primaryTarget, season, sp, ct);

		if (secondaryTargets.Count > 0)
		{
			this.logger.ZLogInfo($"セカンダリサイトによる補完を開始します。({secondaryTargets.Count} サイト)");

			foreach (var secondary in secondaryTargets)
			{
				ct.ThrowIfCancellationRequested();
				this.logger.ZLogInfo($"セカンダリサイト ({secondary.SiteName}) の補完を開始します。");
				var scraper = this.getScraper(secondary.SiteId, season, sp);
				var rawList = await scraper.ScrapeAsync(secondary.TargetUrl, ct);
				masterList = scraper.Translator!.TranslateMerge(rawList, masterList);
				this.logger.ZLogInfo($"補完完了。現在の件数: {masterList.Count} 件");
			}
		}

		await this.complementWithAnnictAsync(masterList, season, annictService, ct);

		this.logger.ZLogInfo($"公式サイトタイトルの取得を開始します。");
		foreach (var work in masterList.Where(w => w.IsImport))
		{
			ct.ThrowIfCancellationRequested();
			await officialPageTitleService.SetOfficialPageTitleAsync(work, ct);
		}
		this.logger.ZLogInfo($"公式サイトタイトルの取得が完了しました。");

		this.logger.ZLogInfo($"全取得完了。合計 {masterList.Count} 件のアニメ作品を構築しました。");

		var stationMasters = await masterRepository.GetBroadcastStationMastersAsync(ct);
		foreach (var work in masterList)
		{
			if (string.IsNullOrEmpty(work.Broadcast))
				continue;

			if (stationMasters.TryGetValue(work.Broadcast, out var master))
			{
				work.Broadcast = master.OfficialName;
				work.MetaBroadcastKana = master.Kana;
			}
			else
			{
				this.logger.ZLogCheck($"放送局マスタ未登録: {work.Broadcast}");
			}
		}

		this.LastSaveResults = await repository.SaveAsync(season, masterList, ct);

		var reporter = sp.GetRequiredService<ScrapingReporter>();
		return await reporter.OutputReportAsync(this.LastSaveResults, season, ct);
	}

	/// <summary>
	/// プライマリサイトからスクレイピングおよび変換を行い、アニメ作品リストを返します。
	/// </summary>
	/// <param name="target">プライマリ偵察対象。</param>
	/// <param name="season">対象クール。</param>
	/// <param name="sp">スコープのサービスプロバイダー。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>変換後のアニメ作品リスト。</returns>
	private async ValueTask<List<AnimeWork>> scrapeAndTranslatePrimaryAsync(ScrapingTarget target, Season season, IServiceProvider sp, CancellationToken ct)
	{
		this.logger.ZLogInfo($"プライマリサイト ({target.SiteName}) の取得を開始します。");
		var scraper = this.getScraper(target.SiteId, season, sp);
		var rawList = await scraper.ScrapeAsync(target.TargetUrl, ct);
		var result = rawList.Select(raw => scraper.Translator!.Translate(raw)).ToList();
		this.logger.ZLogInfo($"プライマリサイトから {result.Count} 件取得・変換しました。");
		return result;
	}

	/// <summary>
	/// 偵察対象サイトに対応するスクレイパーをスコープから資材を調達して生成し返します。
	/// </summary>
	/// <param name="site">対象サイト。</param>
	/// <param name="season">対象クール。</param>
	/// <param name="sp">スコープのサービスプロバイダー。</param>
	private IScraper getScraper(ScrapingSite site, Season season, IServiceProvider sp)
	{
		var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

		return site switch
		{
			ScrapingSite.Animate => new AnimateScraper(
				httpClientFactory.CreateClient(nameof(AnimateScraper)),
				loggerFactory.CreateLogger<AnimateScraper>())
			{
				Translator = new AnimateTranslator(loggerFactory.CreateLogger<AnimateTranslator>()),
			},
			ScrapingSite.Kansou => new KansouScraper(
				httpClientFactory.CreateClient(nameof(KansouScraper)),
				loggerFactory.CreateLogger<KansouScraper>(),
				buildKansouSeasonKeyword(season))
			{
				Translator = new KansouTranslator(loggerFactory.CreateLogger<KansouTranslator>()),
			},
			_ => throw new NotSupportedException($"未対応のサイトです: {site}"),
		};
	}

	/// <summary>
	/// ファイルシステムおよび GIMP マクロでエラーの原因となる記号を除去し、エクスポートファイル名として安全な文字列を返します。
	/// </summary>
	/// <param name="title">サニタイズ対象の文字列。</param>
	/// <returns>サニタイズ済みのファイル名文字列。</returns>
	private static string sanitizeFileName(string title)
	{
		var result = Regex.Replace(title, @"[\\/:*?""<>|!]", string.Empty);
		result = result.Replace(' ', '-');
		result = Regex.Replace(result, @"-{2,}", "-");
		return result.Trim('-', ' ').ToLowerInvariant();
	}

	/// <summary>
	/// kansou.me 向けのクールキーワード文字列（例: "2026年 春"）を構築します。
	/// </summary>
	private static string buildKansouSeasonKeyword(Season season)
	{
		var seasonName = season.SeasonID switch
		{
			SeasonID.Winter => "冬",
			SeasonID.Spring => "春",
			SeasonID.Summer => "夏",
			SeasonID.Autumn => "秋",
			_ => throw new NotSupportedException($"未対応のクールです: {season.SeasonID}"),
		};
		return $"{season.Year}年 {seasonName}";
	}

	/// <summary>
	/// Annict API から取得した作品情報でアニメ作品リストを補完します。
	/// NormalizedTitle による突合でヒットした場合はファイル名・かな・Wiki URL を設定します。
	/// ヒットしない場合は警告ログを出力します。
	/// </summary>
	/// <param name="works">補完対象のアニメ作品リスト。</param>
	/// <param name="season">取得対象のクール。</param>
	/// <param name="annictService">Annict API サービス。</param>
	/// <param name="ct">キャンセルトークン。</param>
	private async ValueTask complementWithAnnictAsync(List<AnimeWork> works, Season season, AnnictService annictService, CancellationToken ct)
	{
		var annictWorks = await annictService.GetSeasonAnimeWorksAsync(season, ct);
		if (annictWorks.Count == 0) return;

		foreach (var work in works)
		{
			ct.ThrowIfCancellationRequested();
			var normalizedWorkTitle = AnimeTitleNormalizer.Normalize(work.Title);
			var match = annictWorks.FirstOrDefault(a =>
				AnimeTitleNormalizer.IsMatch(AnimeTitleNormalizer.Normalize(a.Title), normalizedWorkTitle));

			if (match is not null)
			{
				if (!string.IsNullOrWhiteSpace(match.TitleEn))
					work.ExportFileName = sanitizeFileName(match.TitleEn);
				else
					this.logger.ZLogCheck($"「{work.Title}」の英語名がAnnictに未登録です。");

				if (!string.IsNullOrWhiteSpace(match.TitleKana))
					work.MetaTitleKana = JpStringConverter.ToFullWidthKatakana(match.TitleKana);
				else
					this.logger.ZLogCheck($"「{work.Title}」のカナがAnnictに未登録です。");

				work.WikiUrl = match.WikiUrl;
			}
			else
			{
				this.logger.ZLogCheck($"Annictに「{work.Title}」が存在しません。");
			}
		}
	}
}
