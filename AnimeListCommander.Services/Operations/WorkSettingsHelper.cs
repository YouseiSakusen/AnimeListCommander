using System.IO;
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
			.Replace("{MyTitle}", work.MyTitle)
			.Replace("{OfficialSiteUrl}", work.OfficialSiteUrl)
			.Replace("{OfficialPageTitle}", work.OfficialPageTitle);

		await writer.WriteLineAsync("#SITE_HTML");
		await writer.WriteLineAsync(html);
		await writer.WriteLineAsync();
	}

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

	private static string resolveValue(string headerName, AnimeWork work)
	{
		switch (headerName)
		{
			case "#TITLE":              return work.MyTitle;
			case "#TITLE_RUBY":         return work.Title_Ruby;
			case "#COMPANY":            return work.Company;
			case "#PRODUCTION_LOGO":    return work.Production;
			case "#THEME_SONG":         return work.ThemeSongs;
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

	private static async Task<Dictionary<string, List<string>>> parseExistingSettingsAsync(string settingsPath)
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

	private static bool isHeaderLine(string line) =>
		line.Length > 1 && line[0] == '#' && line[1..].All(c => char.IsAsciiLetterUpper(c) || c == '_');
}
