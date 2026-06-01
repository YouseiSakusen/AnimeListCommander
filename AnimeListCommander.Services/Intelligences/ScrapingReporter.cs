using System.Text;
using AnimeListCommander.Contexts;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// スクレイピング結果をテキストレポートとして出力するクラスです。
/// </summary>
public class ScrapingReporter
{
	private const string LabelNew = "【新】";
	private const string LabelUpdated = "【更】";
	private const string LabelSkipped = "【-】";
	private const string LabelFailed = "【★今回から除外★】要確認";
	private const string Separator = "------------------------------------------------------------";

	private readonly ApplicationContext applicationContext;
	private readonly ILogger<ScrapingReporter> logger;

	/// <summary>
	/// <see cref="ScrapingReporter"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="applicationContext">アプリケーションコンテキスト。</param>
	/// <param name="logger">ロガー。</param>
	public ScrapingReporter(ApplicationContext applicationContext, ILogger<ScrapingReporter> logger)
	{
		this.applicationContext = applicationContext;
		this.logger = logger;
	}

	/// <summary>
	/// 保存結果一覧からテキストレポートを生成してファイルに書き出し、そのフルパスを返します。
	/// </summary>
	/// <param name="results">保存結果一覧。</param>
	/// <param name="allWorks">スクレイピングで取得した全作品（IsImport=false を含む）。Import対象外セクションの出力に使用します。</param>
	/// <param name="season">対象クール。ファイル名に使用します。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>生成されたレポートファイルのフルパス。</returns>
	public async Task<string> OutputReportAsync(IReadOnlyList<SaveResult> results, IReadOnlyList<AnimeWork> allWorks, Season season, CancellationToken ct)
	{
		var outputDir = this.applicationContext.AppConfiguration.SummaryOutputPath;
		Directory.CreateDirectory(outputDir);

		var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
		var filePath = Path.Combine(outputDir, $"scraping-report-{season.Year}-{(int)season.SeasonID}-{timestamp}.txt");

		var sb = new StringBuilder();

		foreach (var result in results)
		{
			var work = result.Work;
			var label = result.Status switch
			{
				SaveStatus.New => LabelNew,
				SaveStatus.Updated => LabelUpdated,
				SaveStatus.Skipped => LabelSkipped,
				SaveStatus.Failed => LabelFailed,
				_ => LabelFailed,
			};

			sb.AppendLine($"{work.AnimateHeaderTitle} {label}");
			sb.AppendLine($"[Title] {work.Title}");
			sb.AppendLine($"[MyTitle] {work.MyTitle}");
			sb.AppendLine($"[MetaTitleKana] {work.MetaTitleKana}");
			sb.AppendLine("");
			sb.AppendLine($"[NormalizedTitle] {work.NormalizedTitle}");
			sb.AppendLine("");
			sb.AppendLine($"[ExportFileName] {work.ExportFileName}");
			sb.AppendLine("");
			sb.AppendLine($"[OfficialSiteUrl] {work.OfficialSiteUrl}");
			sb.AppendLine($"[WikiUrl] {work.WikiUrl}");
			sb.AppendLine($"[IsImport] {work.IsImport}");
			sb.AppendLine();
			sb.AppendLine();
			sb.AppendLine(Separator);
		}

		sb.AppendLine();
		sb.AppendLine($"総実行件数: {results.Count}");
		sb.AppendLine($"  新規登録: {results.Count(r => r.Status == SaveStatus.New)}");
		sb.AppendLine($"  更新あり: {results.Count(r => r.Status == SaveStatus.Updated)}");
		sb.AppendLine($"  変更なし: {results.Count(r => r.Status == SaveStatus.Skipped)}");
		sb.AppendLine($"  要確認:   {results.Count(r => r.Status == SaveStatus.Failed)}");

		var excludedWorks = allWorks.Where(w => !w.IsImport).ToList();
		if (excludedWorks.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine();
			sb.AppendLine("===== Import対象外 =====");
			sb.AppendLine();
			foreach (var work in excludedWorks)
			{
				sb.AppendLine($"タイトル: {work.AnimateHeaderTitle}");
				sb.AppendLine($"理由: {resolveExcludeReason(work)}");
				sb.AppendLine($"Animate公式URL: {work.OfficialSiteUrl}");
				sb.AppendLine($"Wiki URL: {work.WikiUrl}");
				sb.AppendLine($"放送形態: {work.BroadcastText}");
				sb.AppendLine();
				sb.AppendLine(Separator);
			}
		}

		var xcfDiffResults = results.Where(r => r.XcfDiffs.Count > 0).ToList();
		if (xcfDiffResults.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine();
			sb.AppendLine("===== HasXcf作品差分 =====");

			foreach (var r in xcfDiffResults)
			{
				sb.AppendLine();
				sb.AppendLine($"作品：");
				sb.AppendLine(r.Work.MyTitle);

				foreach (var diff in r.XcfDiffs)
				{
					sb.AppendLine();
					sb.AppendLine($"項目：");
					sb.AppendLine(diff.FieldName);
					sb.AppendLine($"DB：");
					sb.AppendLine(diff.DbValue);
					sb.AppendLine($"取得：");
					sb.AppendLine(diff.ScrapedValue);
					sb.AppendLine("反映していません（HasXcf=true）");
				}

				sb.AppendLine();
				sb.AppendLine(Separator);
			}
		}

		await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

		this.logger.ZLogInfo($"レポートを出力しました: {filePath}");

		this.cleanupOldReports(outputDir);

		return filePath;
	}

	/// <summary>
	/// Import対象外の理由を文字列で返します。
	/// </summary>
	private static string resolveExcludeReason(AnimeWork work)
	{
		if (work.AnimateHeaderTitle.Contains("再放送"))
			return "再放送";

		if (work.BroadcastText.Contains("特撮"))
			return "特撮";

		if (string.IsNullOrWhiteSpace(work.OfficialSiteUrl))
			return "公式サイトなし";

		if (work.OfficialSiteUrl.Contains("x.com") || work.OfficialSiteUrl.Contains("twitter.com"))
			return "公式サイトがSNSのみ";

		return "Import対象外";
	}

	/// <summary>
	/// 保持ポリシーに基づき古いレポートファイルを削除します。
	/// </summary>
	private void cleanupOldReports(string outputDir)
	{
		var config = this.applicationContext.AppConfiguration;
		var files = Directory.GetFiles(outputDir, "scraping-report-*.txt")
			.Select(f => new FileInfo(f))
			.OrderByDescending(f => f.LastWriteTime)
			.ToList();

		var cutoffDate = DateTime.Now.AddDays(-config.SummaryRetentionDays);

		var toDelete = files
			.Skip(config.SummaryMinKeepCount)
			.Where(f => f.LastWriteTime < cutoffDate)
			.ToList();

		foreach (var file in toDelete)
		{
			file.Delete();
			this.logger.ZLogInfo($"古いレポートを削除しました: {file.Name}");
		}
	}
}
