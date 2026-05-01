using AnimeListCommander.Contexts;
using AnimeListCommander.Helpers;
using AnimeListCommander.Masters;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;
using ZLogger;

namespace AnimeListCommander.Operations;

/// <summary>
/// HTML変換処理の実行結果を表します。
/// </summary>
public enum HalationGhostHtmlConvertResult
{
	/// <summary>成功。</summary>
	Success,

	/// <summary>対象ファイルが見つかりません。</summary>
	TargetFileNotFound,

	/// <summary>並び順ファイルが見つかりません。</summary>
	OrderFileNotFound,

	/// <summary>アニメ作品IDが見つかりません。</summary>
	AnimeWorkIdNotFound,
}

/// <summary>
/// 展開処理を統括するサービスです。
/// </summary>
public class OperationService
{
	/// <summary>アニメ作品タイトル画像の幅。</summary>
	private const int AnimeWorkTitleWidth = 330;

	/// <summary>アニメ作品タイトル画像の高さ。</summary>
	private const int AnimeWorkTitleHeight = 300;

	/// <summary>アニメ作品タイトル画像のマージン。</summary>
	private const int AnimeWorkTitleMargin = 2;

	/// <summary>アニメ作品タイトルの列数。</summary>
	private const int AnimeWorkTitleColumns = 3;

	/// <summary>クリッカブルマップのファイル名。</summary>
	private const string ClickableMapFileName = "clickableMap.txt";

	/// <summary>アニメリスト並び順ファイルの名前。</summary>
	private const string OrderFileName = "AnimeListOrder.txt";

	private readonly OperationsRepository repository;
	private readonly MasterRepository masterRepository;
	private readonly ApplicationContext applicationContext;
	private readonly ILogger<OperationService> logger;

	/// <summary>
	/// <see cref="OperationService"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="repository">作戦リポジトリ。</param>
	/// <param name="masterRepository">共通マスタリポジトリ。</param>
	/// <param name="applicationContext">アプリケーションコンテキスト。</param>
	/// <param name="logger">ロガー。</param>
	public OperationService(
		OperationsRepository repository,
		MasterRepository masterRepository,
		ApplicationContext applicationContext,
		ILogger<OperationService> logger)
	{
		this.repository = repository;
		this.masterRepository = masterRepository;
		this.applicationContext = applicationContext;
		this.logger = logger;
	}

	/// <summary>
	/// 指定クールのアニメ作品を展開します。
	/// </summary>
	/// <param name="season">展開対象のクール。</param>
	/// <param name="ct">キャンセルトークン。</param>
	public async ValueTask DeployAsync(Season season, CancellationToken ct = default)
	{
		var works = await this.repository.GetAnimeWorksBySeasonAsync(season, ct);

		this.logger.ZLogInformation($"展開対象作品数: {works.Count} 件 ({season.Year}-{(int)season.SeasonID})");

		if (works.Count == 0)
		{
			return;
		}

		// 放送局マスタをメモリ上で適用（DB は書き換えない）
		var stationMasters = await this.masterRepository.GetBroadcastStationMastersAsync(ct);
		foreach (var work in works)
		{
			if (!string.IsNullOrEmpty(work.Broadcast) && stationMasters.TryGetValue(work.Broadcast, out var master))
			{
				work.Broadcast = master.OfficialName;
				work.MetaBroadcastKana = master.Kana;
			}
		}

		var masters = await this.repository.GetWorkSettingItemMastersAsync(ct);

		foreach (var work in works)
		{
			var directoryName = string.IsNullOrWhiteSpace(work.DirectoryName)
				? AnimeTitleNormalizer.ToSafeDirectoryName(work.MyTitle)
				: work.DirectoryName;
			var outputPath = this.applicationContext.AppConfiguration.GetExportPath(season, directoryName);

			this.logger.ZLogInformation($"[Deploy] {work.NormalizedTitle} => {outputPath}");

			await WorkSettingsHelper.WriteWorkSettingsAsync(outputPath, work, masters, this.logger);
		}
	}

	/// <summary>アニメリストHTMLのファイル名。</summary>
	private const string AnimeListHtmlFileName = "animeListHtml.txt";

	/// <summary>
	/// <c>AnimeListOrder.txt</c> の順序に従い、<c>animeListHtml.txt</c> 内の作品紹介ブロックを並べ替えます。
	/// </summary>
	/// <param name="season">対象クール。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>処理結果。</returns>
	public async ValueTask<HalationGhostHtmlConvertResult> SortHtmlByOrderAsync(Season season, CancellationToken ct = default)
	{
		var htmlFilePath = this.applicationContext.AppConfiguration.GetExportPath(season, AnimeListHtmlFileName);
		var orderFilePath = this.applicationContext.AppConfiguration.GetExportPath(season, OrderFileName);

		if (!File.Exists(htmlFilePath))
			return HalationGhostHtmlConvertResult.TargetFileNotFound;

		if (!File.Exists(orderFilePath))
			return HalationGhostHtmlConvertResult.OrderFileNotFound;

		// AnimeListOrder.txt 読み込み（1行目はヘッダーとして無視）
		var orderLines = await File.ReadAllLinesAsync(orderFilePath, ct);
		var orderedIds = orderLines.Skip(1)
			.Select(line => line.Trim())
			.Where(line => !string.IsNullOrEmpty(line))
			.Select(int.Parse)
			.ToList();

		// animeListHtml.txt 読み込み
		var htmlContent = await File.ReadAllTextAsync(htmlFilePath, ct);

		// data-commander-id を持つ h2 タグで分割し、各作品ブロックを抽出
		var blockPattern = new Regex(
			@"(<h2\s[^>]*data-commander-id=""(\d+)""[^>]*>.*?)(?=<h2\s[^>]*data-commander-id=""\d+""|$)",
			RegexOptions.Singleline);

		var blocks = new Dictionary<int, string>();
		foreach (Match match in blockPattern.Matches(htmlContent))
		{
			var id = int.Parse(match.Groups[2].Value);
			blocks[id] = match.Groups[1].Value;
		}

		// IDリスト順に再構築
		var sb = new StringBuilder();
		foreach (var id in orderedIds)
		{
			if (!blocks.TryGetValue(id, out var block))
			{
				this.logger.ZLogWarning($"[SortHtml] 作品ID {id} がHTMLに存在しません。");
				return HalationGhostHtmlConvertResult.AnimeWorkIdNotFound;
			}

			sb.Append(block);
		}

		await File.WriteAllTextAsync(htmlFilePath, sb.ToString(), ct);

		this.logger.ZLogInformation($"[SortHtml] 並べ替え完了: {htmlFilePath}");
		return HalationGhostHtmlConvertResult.Success;
	}

	/// <summary>
	/// クリッカブルマップを生成します。
	/// </summary>
	/// <param name="season">対象クール。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>処理結果。</returns>
	public async ValueTask<HalationGhostHtmlConvertResult> GenerateClickableMapAsync(Season season, CancellationToken ct = default)
	{
		var mapFilePath = this.applicationContext.AppConfiguration.GetExportPath(season, ClickableMapFileName);
		var orderFilePath = this.applicationContext.AppConfiguration.GetExportPath(season, OrderFileName);

		if (!File.Exists(mapFilePath))
			return HalationGhostHtmlConvertResult.TargetFileNotFound;

		if (!File.Exists(orderFilePath))
			return HalationGhostHtmlConvertResult.OrderFileNotFound;

		// AnimeListOrder.txt 読み込み（1行目はヘッダーとして無視）
		// i=0 は見出し画像枠のためダミー値 0 を先頭に挿入してオフセットを合わせる
		var orderLines = await File.ReadAllLinesAsync(orderFilePath, ct);
		var orderedIds = orderLines.Skip(1)
			.Select(line => line.Trim())
			.Where(line => !string.IsNullOrEmpty(line))
			.Select(int.Parse)
			.Prepend(0)
			.ToList();

		// 作品データをIDキーの Dictionary で準備
		var works = await this.repository.GetAnimeWorksBySeasonAsync(season, ct);
		var workMap = works.ToDictionary(w => w.Id);

		// IDリスト内に存在しない作品IDがある場合は中断（ダミー値 0 はスキップ）
		foreach (var id in orderedIds.Where(id => id != 0))
		{
			if (!workMap.ContainsKey(id))
			{
				this.logger.ZLogWarning($"[GenerateMap] 作品ID {id} がDBに存在しません。");
				return HalationGhostHtmlConvertResult.AnimeWorkIdNotFound;
			}
		}

		// <area> タグ群を構築（i=0 は見出し画像枠のためスキップ）
		var mapName = $"{season.Year}{(int)season.SeasonID:D2}-anime";
		var areaSb = new StringBuilder();
		for (var i = 0; i < orderedIds.Count; i++)
		{
			if (i == 0)
				continue;

			var work = workMap[orderedIds[i]];
			var xIndex = i % AnimeWorkTitleColumns;
			var yIndex = i / AnimeWorkTitleColumns;
			var left = xIndex * (AnimeWorkTitleWidth + AnimeWorkTitleMargin * 2) + AnimeWorkTitleMargin;
			var top = yIndex * (AnimeWorkTitleHeight + AnimeWorkTitleMargin * 2) + AnimeWorkTitleMargin;
			var right = left + AnimeWorkTitleWidth;
			var bottom = top + AnimeWorkTitleHeight;

			areaSb.AppendLine($"<area shape=\"rect\" coords=\"{left},{top},{right},{bottom}\" href=\"{work.OfficialSiteUrl}\" target=\"_blank\" alt=\"{work.MyTitle}\" title=\"{work.MyTitle}\">");
		}

		// clickableMap.txt 読み込み
		var mapContent = await File.ReadAllTextAsync(mapFilePath, ct);

		// <a> タグに target="_blank" が存在しない場合のみ追加
		if (!Regex.IsMatch(mapContent, @"<a\s[^>]*target=""_blank"""))
			mapContent = Regex.Replace(mapContent, @"(<a\b[^>]*?)\s*>", "$1 target=\"_blank\">");

		// <img> タグに usemap 属性を付与（既存あれば更新、なければ /> の直前に追加）
		if (Regex.IsMatch(mapContent, @"usemap=""#[^""]*"""))
			mapContent = Regex.Replace(mapContent, @"usemap=""#[^""]*""", $"usemap=\"#{mapName}\"");
		else
			mapContent = Regex.Replace(mapContent, @"(<img\b[^>]*?)\s*/>", $"$1 usemap=\"#{mapName}\" />");

		// 既存の <map>～</map> ブロックを除去（再実行時の重複防止）
		mapContent = Regex.Replace(mapContent, @"\r?\n\r?\n<map\s[^>]*>.*?</map>", string.Empty, RegexOptions.Singleline);

		// </a> の後に空行を1つ挟んで <map>～</map> ブロックを追記
		var mapBlock = new StringBuilder();
		mapBlock.AppendLine($"<map name=\"{mapName}\">");
		mapBlock.Append(areaSb);
		mapBlock.Append("</map>");

		mapContent = Regex.Replace(mapContent, @"(</a>)", $"$1{Environment.NewLine}{Environment.NewLine}{mapBlock}");

		await File.WriteAllTextAsync(mapFilePath, mapContent, ct);

		this.logger.ZLogInformation($"[GenerateMap] 生成完了: {mapFilePath}");
		return HalationGhostHtmlConvertResult.Success;
	}
}
