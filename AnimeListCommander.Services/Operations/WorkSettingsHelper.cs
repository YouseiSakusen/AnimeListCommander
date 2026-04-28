using System.Text;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Operations;

/// <summary>
/// 作品設定ファイルの書き出しを担う静的ユーティリティクラスです。
/// </summary>
public static class WorkSettingsHelper
{
	private const string SettingsFileName = "work-settings.txt";
	private const string TemplateFileName = "deploy-template.html";

	/// <summary>
	/// 指定ディレクトリに作品設定ファイルを書き出します。
	/// </summary>
	/// <param name="directoryPath">出力先ディレクトリパス。</param>
	/// <param name="work">対象アニメ作品。</param>
	/// <param name="masters">作品設定項目マスタ一覧。</param>
	/// <param name="logger">ロガー。</param>
	public static async ValueTask WriteWorkSettingsAsync(
		string directoryPath,
		AnimeWork work,
		IReadOnlyList<WorkSettingItem> masters,
		ILogger logger)
	{
		Directory.CreateDirectory(directoryPath);

		var settingsPath = Path.Combine(directoryPath, SettingsFileName);
		var existingValues = await parseExistingSettingsAsync(settingsPath);
		backupIfExists(settingsPath);

		var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		await using var writer = new StreamWriter(settingsPath, append: false, encoding);

		foreach (var master in masters.OrderBy(m => m.DisplayOrder))
		{
			foreach (var line in resolveLines(master, work, existingValues))
			{
				await writer.WriteLineAsync(line);
			}
		}

		var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TemplateFileName);
		if (!File.Exists(templatePath))
		{
			logger.ZLogWarning($"{TemplateFileName} が見つかりません。HTML追記をスキップします。(path={templatePath})");
			return;
		}

		var template = await File.ReadAllTextAsync(templatePath, encoding);
		var html = template
			.Replace("{AnimeWorkId}", work.Id.ToString())
			.Replace("{MyTitle}", work.MyTitle)
			.Replace("{OfficialSiteUrl}", work.OfficialSiteUrl)
			.Replace("{OfficialPageTitle}", work.OfficialPageTitle);

		await writer.WriteLineAsync("#AnimeWorkId");
		await writer.WriteLineAsync(work.Id.ToString());
		await writer.WriteLineAsync();

		await writer.WriteLineAsync("#SITE_HTML");
		await writer.WriteLineAsync(html);
		await writer.WriteLineAsync();
	}

	/// <summary>
	/// 既存の設定ファイルをタイムスタンプ付きのファイル名にリネームしてバックアップします。
	/// ファイルが存在しない場合は何もしません。
	/// </summary>
	/// <param name="settingsPath">バックアップ対象の設定ファイルパス。</param>
	private static void backupIfExists(string settingsPath)
	{
		if (!File.Exists(settingsPath))
		{
			return;
		}

		var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
		var backupPath = Path.Combine(
			Path.GetDirectoryName(settingsPath)!,
			$"work-settings_{timestamp}.txt");

		File.Move(settingsPath, backupPath, overwrite: true);
	}

	/// <summary>
	/// マスタ設定項目に対応する出力行を列挙します。
	/// 上書き禁止かつ既存値がある場合は既存値を、それ以外はスクレイピング結果を使用します。
	/// </summary>
	/// <param name="master">出力対象の作品設定項目マスタ。</param>
	/// <param name="work">出力元のアニメ作品データ。</param>
	/// <param name="existingValues">既存の設定ファイルから読み込んだヘッダーと値のマッピング。</param>
	/// <returns>設定ファイルに書き出す行のシーケンス。</returns>
	private static IEnumerable<string> resolveLines(
		WorkSettingItem master,
		AnimeWork work,
		IReadOnlyDictionary<string, List<string>> existingValues)
	{
		existingValues.TryGetValue(master.HeaderName, out var existingLines);
		var useExisting = !master.IsOverwriteAllowed && existingLines is { Count: > 0 };

		switch (master.HeaderName)
		{
			case "#CAST":
				yield return master.HeaderName;
				if (useExisting)
				{
					foreach (var line in existingLines!)
						yield return line;
				}
				else
				{
					foreach (var cast in work.Casts.Where(c => c.IsExport).OrderBy(c => c.SortOrder))
						yield return cast.Name;
				}
				yield return string.Empty;
				yield break;

			case "#STAFF":
				yield return master.HeaderName;
				if (useExisting)
				{
					foreach (var line in existingLines!)
						yield return line;
				}
				else
				{
					foreach (var staff in work.Staffs.Where(s => s.IsExport).OrderBy(s => s.SortOrder))
					{
						yield return staff.Role;
						yield return staff.Name;
					}
				}
				yield return string.Empty;
				yield break;

			default:
					yield return master.HeaderName;
					if (useExisting)
					{
						foreach (var line in existingLines!)
							yield return line;
					}
					else
					{
						yield return resolveValue(master.HeaderName, work);
					}
					yield return string.Empty;
					break;
		}
	}

	/// <summary>
	/// ヘッダー名に対応するアニメ作品データの値を返します。
	/// </summary>
	/// <param name="headerName">設定ファイルのヘッダー名（例：<c>#TITLE</c>）。</param>
	/// <param name="work">値の取得元となるアニメ作品データ。</param>
	/// <returns>ヘッダーに対応する文字列値。対応するヘッダーが存在しない場合は空文字を返します。</returns>
	private static string resolveValue(string headerName, AnimeWork work)
	{
		switch (headerName)
		{
			case "#TITLE":              return work.MyTitle;
			case "#TITLE_RUBY":         return work.Title_Ruby;
			case "#COMPANY":            return work.Company;
			case "#PRODUCTION_LOGO":    return work.Production;
			// 外部ツール（画像生成マクロ等）のテンプレートとして項目が必要なため、
			// 未設定の場合もデフォルト値を出力する。
			case "#THEME_SONG":
			{
				var lines = string.IsNullOrWhiteSpace(work.ThemeSongs)
					? []
					: work.ThemeSongs.Split(["\r\n", "\n"], StringSplitOptions.None);

				if (lines.Length == 0)
					return "OP：\nED：";

				if (lines.Length >= 2)
					return work.ThemeSongs;

				// 1行のみ
				var single = lines[0];
				if (single.StartsWith("主題歌："))
					return single;
				if (single.StartsWith("OP："))
					return single + "\nED：";
				if (single.StartsWith("ED："))
					return "OP：\n" + single;

				return single;
			}
			case "#ORIGINAL":           return work.Original;
			case "#BROADCAST_TEXT":     return work.BroadcastText;
			case "#BROADCAST_LOGO":     return work.Broadcast;
			case "#FIRST_BROADCAST":    return work.FirstBroadcast;
			case "#EXPORT_FILENAME":    return work.ExportFileName;
			case "#META_TITLE_KANA":    return work.MetaTitleKana;
			case "#META_BROADCAST_KANA": return work.MetaBroadcastKana;
			default:                    return string.Empty;
		}
	}

	/// <summary>
	/// 既存の設定ファイルを読み込み、ヘッダーをキー・対応する行リストを値とする辞書を返します。
	/// ファイルが存在しない場合は空の辞書を返します。
	/// </summary>
	/// <param name="settingsPath">読み込み対象の設定ファイルパス。</param>
	/// <returns>ヘッダー名と行リストのマッピング。</returns>
	private static async ValueTask<Dictionary<string, List<string>>> parseExistingSettingsAsync(string settingsPath)
	{
		var result = new Dictionary<string, List<string>>();
		if (!File.Exists(settingsPath))
			return result;

		var lines = await File.ReadAllLinesAsync(settingsPath);
		string? currentHeader = null;
		var currentLines = new List<string>();

		void flushSection()
		{
			if (currentHeader is null)
				return;
			while (currentLines.Count > 0 && string.IsNullOrEmpty(currentLines[^1]))
				currentLines.RemoveAt(currentLines.Count - 1);
			result[currentHeader] = new List<string>(currentLines);
		}

		foreach (var line in lines)
		{
			if (isHeaderLine(line))
			{
				flushSection();
				currentHeader = line;
				currentLines.Clear();
			}
			else if (currentHeader is not null)
			{
				currentLines.Add(line);
			}
		}

		flushSection();
		return result;
	}

	/// <summary>
	/// 指定した行が設定ファイルのヘッダー行（<c>#</c> で始まり英大文字とアンダースコアのみで構成される行）かどうかを判定します。
	/// </summary>
	/// <param name="line">判定対象の行文字列。</param>
	/// <returns>ヘッダー行の場合は <see langword="true"/>。</returns>
	private static bool isHeaderLine(string line) =>
		line.Length > 1 && line[0] == '#' && line[1..].All(c => char.IsAsciiLetterUpper(c) || c == '_');
}
