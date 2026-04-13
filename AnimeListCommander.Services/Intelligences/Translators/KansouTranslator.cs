using AnimeListCommander.Helpers;
using HalationGhost.Utilities;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using ZLogger;

namespace AnimeListCommander.Intelligences.Translators;

/// <summary>
/// kansou.me のスクレイピング生データを <see cref="AnimeWork"/> に変換するトランスレーターです。
/// </summary>
public class KansouTranslator : TranslatorBase
{
	/// <summary>
	/// ロガー。
	/// </summary>
	private readonly ILogger<KansouTranslator> logger;

	private static readonly Regex broadcastLineRegex =
		new(@"^(.+?)：(\d{1,2}/\d{1,2})\s*[（(]?[月火水木金土日][）)]?\s*(\d{1,2}:\d{2})", RegexOptions.Compiled);

	private static readonly Regex bracketCleanRegex =
		new(@"\p{Ps}[^\p{Pe}]*\p{Pe}", RegexOptions.Compiled);

	/// <summary>放送局名の略称・正式名マッピング。</summary>
	private static readonly IReadOnlyDictionary<string, string> stationNameMap =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["MX"] = "TOKYO MX",
		};

	private record ParsedBroadcastEntry(string CleanStation, DateTime SortDateTime, DateTime ScheduleDate);

	/// <summary>
	/// <see cref="KansouTranslator"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="logger">ロガー。</param>
	public KansouTranslator(ILogger<KansouTranslator> logger)
	{
		this.logger = logger;
	}

	/// <summary>
	/// スクレイピングされた未構造のデータを、アプリケーションで扱う <see cref="AnimeWork"/> 構造体に変換します。
	/// </summary>
	/// <param name="rawData">スクレイピング済みの生データ。</param>
	/// <returns>変換後のアニメ作品情報。</returns>
	protected override AnimeWork translateCore(ScrapedAnimeInformation rawData)
	{
		this.logger.ZLogInfo($"変換中: {rawData.Title}");
		var work = new AnimeWork
		{
			Title = rawData.Title,
			MyTitle = JpStringConverter.ToHalfWidthAlphanumeric(rawData.Title),
			OfficialSiteUrl = rawData.OfficialSiteUrl,
		};

		// SNSのみの作品は正規の公式サイト無しとして扱う
		if (string.IsNullOrWhiteSpace(work.OfficialSiteUrl)
			|| work.OfficialSiteUrl.Contains("x.com",       StringComparison.OrdinalIgnoreCase)
			|| work.OfficialSiteUrl.Contains("twitter.com", StringComparison.OrdinalIgnoreCase))
		{
			work.IsImport = false;
		}

		applyBroadcastInfo(rawData, work);
		return work;
	}

	/// <inheritdoc/>
	public override List<AnimeWork> TranslateMerge(List<ScrapedAnimeInformation> rawDataList, List<AnimeWork> currentList)
	{
		foreach (var rawData in rawDataList)
		{
			var normalizedTitle = AnimeTitleNormalizer.Normalize(rawData.Title);
			var match = currentList.FirstOrDefault(w => AnimeTitleNormalizer.IsMatch(w.NormalizedTitle, normalizedTitle));

			if (match is null)
			{
				var newWork = this.Translate(rawData);
				currentList.Add(newWork);
				this.logger.ZLogInfo($"Kansou: 新規作品を追加しました: {rawData.Title}");
				continue;
			}

			applyBroadcastInfo(rawData, match);
			this.logger.ZLogInfo($"Kansou: 補完完了: {match.Title}");
		}

		return currentList;
	}

	/// <summary>
	/// 開始日と放送局情報を <see cref="AnimeWork"/> に適用します。
	/// </summary>
	/// <param name="rawData">スクレイピング済みの生データ。</param>
	/// <param name="work">値を設定する対象の <see cref="AnimeWork"/>。</param>
	private static void applyBroadcastInfo(ScrapedAnimeInformation rawData, AnimeWork work)
	{
		var confirmed    = parseBroadcastLines(rawData.KansouBroadcastRawText);
		var undetermined = parseUndeterminedStations(rawData.KansouBroadcastRawText);

		// Kansou に放送情報がない場合は既存の値（Animate 由来など）を上書きしない
		if (confirmed.Count == 0 && undetermined.Count == 0)
			return;

		if (confirmed.Count > 0)
		{
			var sorted      = confirmed.OrderBy(e => e.SortDateTime).ToList();
			var fastest     = sorted[0];
			var displayHour = fastest.SortDateTime.Hour <= 4
				? fastest.SortDateTime.Hour + 24
				: fastest.SortDateTime.Hour;
			work.FirstBroadcast = $"{fastest.ScheduleDate:M/d}～ 毎週{fastest.ScheduleDate.ToString("ddd")}曜 {displayHour:D2}:{fastest.SortDateTime.Minute:D2}";
			work.Broadcast      = fastest.CleanStation;
			work.BroadcastText  = string.Join(", ", sorted.Skip(1).Select(e => e.CleanStation).Concat(undetermined));
		}
		else
		{
			// 確定日程の局なし、未確定局のみ
			work.FirstBroadcast = string.Empty;
			work.Broadcast      = undetermined[0];
			work.BroadcastText  = string.Join(", ", undetermined.Skip(1));
		}
	}

	private static List<ParsedBroadcastEntry> parseBroadcastLines(string? rawText)
	{
		if (string.IsNullOrWhiteSpace(rawText) || rawText == "-")
			return [];

		var result = new List<ParsedBroadcastEntry>();
		var currentYear = DateTime.Now.Year;

		foreach (var line in rawText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			if (line.Contains("--/--")) continue;

			var m = broadcastLineRegex.Match(line);
			if (!m.Success) continue;

			var rawStation = m.Groups[1].Value;
			var datePart  = m.Groups[2].Value;
			var timePart  = m.Groups[3].Value;

			var timeSplit = timePart.Split(':');
			if (!int.TryParse(timeSplit[0], out var hours))   continue;
			if (!int.TryParse(timeSplit[1], out var minutes)) continue;

			var dateSplit = datePart.Split('/');
			if (!int.TryParse(dateSplit[0], out var month)) continue;
			if (!int.TryParse(dateSplit[1], out var day))   continue;

			DateTime baseDate;
			try { baseDate = new DateTime(currentYear, month, day); }
			catch { continue; }

			// 00:00〜04:59 は前日深夜枠（28時間制補正）
			var scheduleDate = hours is >= 0 and <= 4
				? baseDate.AddDays(-1)
				: baseDate;
			var sortDateTime = baseDate.AddHours(hours).AddMinutes(minutes);

			var cleanStation = normalizeStation(rawStation);
			result.Add(new ParsedBroadcastEntry(cleanStation, sortDateTime, scheduleDate));
		}

		return result;
	}

	private static List<string> parseUndeterminedStations(string? rawText)
	{
		if (string.IsNullOrWhiteSpace(rawText) || rawText == "-")
			return [];

		var result = new List<string>();
		foreach (var line in rawText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			if (!line.Contains("--/--")) continue;
			var colonIdx = line.IndexOf('：');
			if (colonIdx <= 0) continue;
			var station = normalizeStation(line[..colonIdx]);
			if (!string.IsNullOrWhiteSpace(station))
				result.Add(station);
		}
		return result;
	}

	private static string normalizeStation(string rawStation)
	{
		var name = bracketCleanRegex.Replace(rawStation, "").Replace("系", "").Trim();
		return stationNameMap.TryGetValue(name, out var mapped) ? mapped : name;
	}
}
