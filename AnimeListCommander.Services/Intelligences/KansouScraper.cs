using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AnimeListCommander.Intelligences.Translators;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// kansou.me 向けのスクレイパー実装です。
/// 指定されたクールのキーワードを含む &lt;h2&gt; を特定し、
/// 直後の &lt;table&gt; から作品データを抽出します。
/// </summary>
public class KansouScraper : IScraper
{
	private readonly HttpClient httpClient;
	private readonly ILogger<KansouScraper> logger;
	private readonly string seasonKeyword;

	/// <inheritdoc/>
	public ITranslator? Translator { get; init; }

	/// <summary>
	/// <see cref="KansouScraper"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="httpClient">HTTP クライアント。</param>
	/// <param name="logger">ロガー。</param>
	/// <param name="seasonKeyword">対象クールのキーワード（例: "2026年 春"）。</param>
	public KansouScraper(HttpClient httpClient, ILogger<KansouScraper> logger, string seasonKeyword)
	{
		this.httpClient = httpClient;
		this.logger = logger;
		this.seasonKeyword = seasonKeyword;
	}

	/// <inheritdoc/>
	public async Task<List<ScrapedAnimeInformation>> ScrapeAsync(string url, CancellationToken ct = default)
	{
		var html = await this.httpClient.GetStringAsync(url, ct);

		var config = Configuration.Default;
		using var context = BrowsingContext.New(config);
		var document = await context.OpenAsync(req => req.Content(html));

		var targetH2 = document.QuerySelectorAll("h2")
			.FirstOrDefault(h => h.TextContent.Contains(this.seasonKeyword));

		if (targetH2 is null)
		{
			this.logger.ZLogError($"対象クールの <h2> が見つかりませんでした。キーワード: \"{this.seasonKeyword}\" URL: {url}");
			return [];
		}

		this.logger.ZLogInfo($"対象 <h2> を検出しました: \"{targetH2.TextContent.Trim()}\"");

		var targetTable = findNextTable(targetH2);
		if (targetTable is null)
		{
			this.logger.ZLogError($"<h2> の後続に <table> が見つかりませんでした。<h2>: \"{targetH2.TextContent.Trim()}\" URL: {url}");
			return [];
		}

		var results = this.parseTable(targetTable);
		this.logger.ZLogInfo($"{url} のクール \"{this.seasonKeyword}\" から {results.Count} 件解析しました。");
		return results;
	}

	/// <summary>
	/// 指定要素の次の兄弟を辿り、最初に出現する &lt;table&gt; を返します。
	/// &lt;h2&gt; に到達した場合は次セクションへ越境したと判断し null を返します。
	/// </summary>
	private static IElement? findNextTable(IElement startElement)
	{
		var sibling = startElement.NextElementSibling;
		while (sibling is not null)
		{
			if (sibling.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
				return sibling;
			if (sibling.TagName.Equals("H2", StringComparison.OrdinalIgnoreCase))
				return null;
			sibling = sibling.NextElementSibling;
		}
		return null;
	}

	/// <summary>
	/// テーブルのヘッダー行を動的に解析して列インデックスを特定し、
	/// 各データ行からアニメ情報を抽出して返します。
	/// </summary>
	private List<ScrapedAnimeInformation> parseTable(IElement table)
	{
		var rows = table.QuerySelectorAll("tr").ToList();
		if (rows.Count == 0)
		{
			this.logger.ZLogWarning($"<table> に行が存在しません。");
			return [];
		}

		var headerCells = rows[0].QuerySelectorAll("th, td")
			.Select(c => c.TextContent.Trim())
			.ToList();

		var titleIndex = headerCells.FindIndex(h => h.Contains("タイトル") || h.Contains("作品名"));
		var startDayIndex = headerCells.FindIndex(h => h.Contains("放送開始日"));
		var broadcastIndex = headerCells.FindIndex(h => h.Contains("放送局"));

		if (titleIndex < 0)
		{
			this.logger.ZLogError($"ヘッダー行に作品タイトル列が見つかりませんでした。検出されたヘッダー: [{string.Join(", ", headerCells)}]");
			return [];
		}

		this.logger.ZLogInfo($"列インデックス → 作品タイトル: {titleIndex}, 放送開始日: {startDayIndex}, 放送局: {broadcastIndex}");

		var results = new List<ScrapedAnimeInformation>();

		foreach (var row in rows.Skip(1))
		{
			var cells = row.QuerySelectorAll("td").ToList();
			if (cells.Count == 0) continue;

			var info = new ScrapedAnimeInformation();

			if (titleIndex < cells.Count)
			{
				var titleCell = cells[titleIndex];
				var anchor = titleCell.QuerySelector("a");
				if (anchor is not null)
				{
					info.Title = anchor.TextContent.ReplaceLineEndings(" ").Trim();
					info.OfficialSiteUrl = anchor.GetAttribute("href") ?? string.Empty;
				}
				else
				{
					info.Title = titleCell.TextContent.ReplaceLineEndings(" ").Trim();
				}
			}

			if (startDayIndex >= 0 && startDayIndex < cells.Count)
				info.KansouBroadcastStartDay = cells[startDayIndex].TextContent.Trim();

			if (broadcastIndex >= 0 && broadcastIndex < cells.Count)
				info.KansouBroadcastRawText = extractCleanedBroadcastText(cells[broadcastIndex]);

			if (!string.IsNullOrWhiteSpace(info.Title))
			{
				this.logger.ZLogInfo($"解析中: {info.Title}");
				results.Add(info);
			}
		}

		return results;
	}

	/// <summary>
	/// 放送局セルからテキストを抽出し、行頭の「□」等の記号をトリムして返します。
	/// &lt;br&gt; タグは改行（\n）に変換します。
	/// </summary>
	private static string extractCleanedBroadcastText(IElement cell)
	{
		var sb = new StringBuilder();
		buildText(cell, sb);

		var lines = sb.ToString()
			.Replace("\r\n", "\n")
			.Replace("\r", "\n")
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.TrimStart('□').Trim())
			.Where(line => line.Length > 0);

		return string.Join("\n", lines);
	}

	/// <summary>
	/// ノードツリーを再帰走査してテキストを構築します。&lt;br&gt; 要素は \n として追記します。
	/// </summary>
	private static void buildText(IElement element, StringBuilder sb)
	{
		foreach (var node in element.ChildNodes)
		{
			if (node is IText textNode)
				sb.Append(textNode.Data);
			else if (node is IElement child)
			{
				if (child.TagName.Equals("BR", StringComparison.OrdinalIgnoreCase))
					sb.Append('\n');
				else
					buildText(child, sb);
			}
		}
	}
}
