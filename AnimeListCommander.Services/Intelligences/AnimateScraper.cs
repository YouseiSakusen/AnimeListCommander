using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AnimeListCommander.Intelligences.Translators;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// アニメイトタイムス（animatetimes.com）向けのスクレイパー実装です。
/// 記事内の各アニメブロックの &lt;table&gt; を走査し、
/// 行ラベル（td）をキーとして対応する値（th）を抽出します。
/// </summary>
public class AnimateScraper : IScraper
{
	private readonly HttpClient httpClient;
	private readonly ILogger<AnimateScraper> logger;

	/// <summary>
	/// このスクレイパーに関連付けられたトランスレーターを取得または初期化します。
	/// </summary>
	public ITranslator? Translator { get; init; }

	/// <summary>
	/// <see cref="AnimateScraper"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="httpClient">HTTP クライアント。</param>
	/// <param name="logger">ロガー。</param>
	public AnimateScraper(HttpClient httpClient, ILogger<AnimateScraper> logger)
	{
		this.httpClient = httpClient;
		this.logger = logger;
	}

	/// <inheritdoc/>
	public async Task<List<ScrapedAnimeInformation>> ScrapeAsync(string url, CancellationToken ct = default)
	{
		var html = await this.httpClient.GetStringAsync(url, ct);

		var config = Configuration.Default;
		using var context = BrowsingContext.New(config);
		var document = await context.OpenAsync(req => req.Content(html));

		var results = new List<ScrapedAnimeInformation>();

		foreach (var table in document.QuerySelectorAll("table"))
		{
			var anime = this.parseTable(table, document);
			if (anime is not null)
				results.Add(anime);
		}

		this.logger.ZLogInfo($"{url} から {results.Count} 件解析しました。");
		return results;
	}

	/// <summary>
	/// table 要素の tr 行をループし、td ラベルに対応する th の値をアニメ情報に格納します。
	/// 「作品名」行が存在する場合のみ結果を返します。
	/// </summary>
	private ScrapedAnimeInformation? parseTable(IElement table, IDocument document)
	{
		var info = new ScrapedAnimeInformation();
		var hasTitle = false;

		// テーブル前の <h2> を先読みする（「再放送」等の注記は見出し側にのみ含まれる場合がある）
		info.AnimateHeaderTitle = extractSectionHeading(document, table);

		foreach (var tr in table.QuerySelectorAll("tr"))
		{
			var td = tr.QuerySelector("td");
			var th = tr.QuerySelector("th");
			if (td is null || th is null) continue;

			var label = td.TextContent.Trim();
			switch (label)
			{
				case "作品名":
					// テーブル内のテキストをそのまま格納する（h2 による上書きは行わない）
					info.Title = this.extractTextWithLineBreaks(th);
					this.logger.ZLogInfo($"解析中: {info.Title}");
					hasTitle = true;
					break;
				case "キャスト":
					info.Cast = this.extractTextWithLineBreaks(th);
					break;
				case "スタッフ":
					info.Staff = this.extractTextWithLineBreaks(th);
					break;
				case "主題歌":
					info.ThemeSong = this.extractTextWithLineBreaks(th);
					break;
					case "公式サイト":
						info.OfficialSiteUrl = th.QuerySelector("a")?.GetAttribute("href") ?? string.Empty;
						break;
					case "スケジュール":
						info.AnimateScheduleRawText = this.extractTextWithLineBreaks(th);
						break;
				}
		}

		if (hasTitle && info.OfficialSiteUrl == string.Empty)
			info.OfficialSiteUrl = this.findOfficialSiteUrlFromSiblings(table);

		return hasTitle ? info : null;
	}

	/// <summary>
	/// table の後続の兄弟要素を &lt;h2&gt; に到達するまで走査し、
	/// 「公式サイト」テキストを持つ &lt;a&gt; タグの href を返します。
	/// 見つからない場合は空文字を返します。
	/// </summary>
	private string findOfficialSiteUrlFromSiblings(IElement table)
	{
		var sibling = table.NextElementSibling;

		while (sibling is not null
			&& !sibling.TagName.Equals("H2", StringComparison.OrdinalIgnoreCase))
		{
			var href = this.findOfficialSiteHref(sibling);
			if (href.Length > 0)
				return href;

			sibling = sibling.NextElementSibling;
		}

		return string.Empty;
	}

	/// <summary>
	/// 要素自体または子孫の &lt;a&gt; タグから「公式サイト」テキストを持つ href を返します。
	/// 見つからない場合は空文字を返します。
	/// </summary>
	private string findOfficialSiteHref(IElement element)
	{
		if (element.TagName.Equals("A", StringComparison.OrdinalIgnoreCase)
			&& element.TextContent.Contains("公式サイト"))
		{
			var href = element.GetAttribute("href");
			if (href is not null && href.Length > 0)
				return href;
		}

		foreach (var anchor in element.QuerySelectorAll("a"))
		{
			if (anchor.TextContent.Contains("公式サイト"))
			{
				var href = anchor.GetAttribute("href");
				if (href is not null && href.Length > 0)
					return href;
			}
		}

		return string.Empty;
	}

	/// <summary>
	/// ドキュメント内の &lt;h2&gt; と &lt;table&gt; をドキュメント順に走査し、
	/// 対象テーブルの直前にある &lt;h2&gt; のテキストを返します。
	/// 見つからない場合は空文字を返します。
	/// </summary>
	/// <param name="document">走査対象のドキュメント。</param>
	/// <param name="table">基点となるテーブル要素。</param>
	/// <returns>直前の &lt;h2&gt; のテキスト。見つからない場合は空文字。</returns>
	private static string extractSectionHeading(IDocument document, IElement table)
	{
		var heading = string.Empty;
		foreach (var element in document.QuerySelectorAll("h2, table"))
		{
			if (element == table)
				break;
			if (element.TagName.Equals("H2", StringComparison.OrdinalIgnoreCase))
				heading = element.TextContent.Trim();
		}
		return heading;
	}

	/// <summary>
	/// 要素内のテキストを抽出します。&lt;br&gt; タグは改行（\n）に変換します。
	/// </summary>
	private string extractTextWithLineBreaks(IElement element)
	{
		var sb = new StringBuilder();
		this.buildText(element, sb);
		return sb.ToString().Trim();
	}

	/// <summary>
	/// ノードツリーを再帰走査しテキストを構築します。br 要素は \n として追記します。
	/// </summary>
	private void buildText(IElement element, StringBuilder sb)
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
					this.buildText(child, sb);
			}
		}
	}
}
