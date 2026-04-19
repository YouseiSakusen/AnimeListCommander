using HalationGhost.Utilities;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using ZLogger;

namespace AnimeListCommander.Intelligences.Translators;

/// <summary>
/// アニメイトタイムスのスクレイピング生データを <see cref="AnimeWork"/> に変換するトランスレーターです。
/// </summary>
public class AnimateTranslator : TranslatorBase
{
	/// <summary>
	/// ロガー。
	/// </summary>
	private readonly ILogger<AnimateTranslator> logger;

	/// <summary>
	/// 主題歌情報をパースするための正規表現。
	/// 「OP：「曲名」アーティスト名」または「「曲名」アーティスト名」の形式から、接頭辞・曲名・アーティストを抽出します。
	/// </summary>
	private static readonly Regex themeSongRegex =
		new(@"^((?:.+?：)?)「([^」]+)」\s*(.+)$", RegexOptions.Compiled);

	/// <summary>
	/// スタッフ情報から制作会社を抽出するための正規表現。
	/// 「制作：」または「アニメーション制作：」で始まる行にマッチします。
	/// </summary>
	private static readonly Regex productionRegex =
		new(@"^(?:アニメーション制作|制作)：(.+)$", RegexOptions.Compiled);

	/// <summary>
	/// スケジュール文字列の末尾語（「にて」「ほか」「など」）を除去するための正規表現。
	/// </summary>
	private static readonly Regex stationEndRegex =
		new(@"(にて|ほか|など)+$", RegexOptions.Compiled);

	/// <summary>
	/// 放送局名から不要な付加情報（「公式」接頭辞・「系全国XX局ネット」「系列XX局ネット」「系」「全国」「XX局ネット」「「...」枠」「「...」」）を除去するための正規表現。
	/// </summary>
	private static readonly Regex stationNoiseRegex =
		new(@"^公式|系全国\d+局ネット|系列\d+局ネット|系|\d+局ネット|全国|「[^」]*」枠?", RegexOptions.Compiled);

	/// <summary>放送局名の略称・正式名マッピング。</summary>
	private static readonly IReadOnlyDictionary<string, string> stationNameMap =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["テレ東"] = "テレビ東京",
		};

	/// <summary>
	/// 主題歌が OP / ED / 主題歌 / INSERT / 挿入歌 等のラベルで始まっているかを判定するための正規表現。
	/// </summary>
	private static readonly Regex themeLabelRegex =
		new(@"^(?:OP|ED|主題歌|INSERT|挿入歌)\d*[：:]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	/// <summary>
	/// <see cref="AnimateTranslator"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="logger">ロガー。</param>
	public AnimateTranslator(ILogger<AnimateTranslator> logger)
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
			AnimateHeaderTitle = rawData.AnimateHeaderTitle,
			// タイトル内の英数字のみを半角化し、画像出力用のタイトルとして保持
			MyTitle = JpStringConverter.ToHalfWidthAlphanumeric(rawData.Title),
			OfficialSiteUrl = rawData.OfficialSiteUrl,
			// 再放送（見出し由来）、または公式サイト URL が未設定の場合はインポート対象外とする
			IsImport = !rawData.AnimateHeaderTitle.Contains("再放送") && !string.IsNullOrWhiteSpace(rawData.OfficialSiteUrl),
		};

		parseThemeSongs(rawData.ThemeSong, work);
		parseStaff(rawData.Staff, work);
		parseCast(rawData.Cast, work);

		if (!string.IsNullOrWhiteSpace(rawData.AnimateScheduleRawText))
			applyScheduleBroadcastInfo(rawData.AnimateScheduleRawText, work);

		return work;
	}

	/// <summary>
	/// アニメイトはプライマリサイト専用のため、このメソッドは呼び出されない想定です。
	/// </summary>
	/// <param name="rawDataList">スクレイピング生データのリスト。</param>
	/// <param name="currentList">統合先となる既存のアニメ作品リスト。</param>
	/// <returns>渡された <paramref name="currentList"/> をそのまま返します。</returns>
	public override List<AnimeWork> TranslateMerge(List<ScrapedAnimeInformation> rawDataList, List<AnimeWork> currentList)
		=> currentList;

	/// <summary>
	/// 主題歌文字列をパースし、指定された形式（アーティスト名「曲名」）に入れ替えて設定します。
	/// </summary>
	/// <param name="themeSong">スクレイピングされた主題歌の改行区切りテキスト。</param>
	/// <param name="work">値を設定する対象の <see cref="AnimeWork"/> オブジェクト。</param>
	private static void parseThemeSongs(string themeSong, AnimeWork work)
	{
		var results = new List<string>();
		foreach (var line in splitLines(themeSong))
		{
			// 情報が確定していない行はスキップ
			if (line.Contains("未発表")) continue;

			var match = themeSongRegex.Match(line);
			if (match.Success)
			{
				var prefix = match.Groups[1].Value; // 「OP：」など
				var songTitle = match.Groups[2].Value; // 「曲名」
				var artist = match.Groups[3].Value; // 「アーティスト名」

				// 「アーティスト名「曲名」」の順序に入れ替えて格納
				results.Add($"{prefix}{artist}「{songTitle}」");
			}
			else
			{
				results.Add(line);
			}
		}
		if (results.Count == 1 && !themeLabelRegex.IsMatch(results[0]))
			results[0] = "主題歌：" + results[0];
		work.ThemeSongs = string.Join("\n", results);
	}

	/// <summary>
	/// スタッフ情報をパースし、制作会社の抽出およびスタッフリストの生成を行います。
	/// </summary>
	/// <param name="staff">スクレイピングされたスタッフ情報の改行区切りテキスト。</param>
	/// <param name="work">値を設定する対象の <see cref="AnimeWork"/> オブジェクト。</param>
	private static void parseStaff(string staff, AnimeWork work)
	{
		var staffList = new List<StaffInfo>();
		var productions = new List<string>();
		var sortOrder = 1;

		foreach (var line in splitLines(staff))
		{
			if (line.Contains("未発表")) continue;

			// 1. 制作会社行の判定 → Production へ（Staffs には入れない）
			var productionMatch = productionRegex.Match(line);
			if (productionMatch.Success)
			{
				productions.Add(productionMatch.Groups[1].Value.Trim());
				continue;
			}

			// 2. 原作行の判定 → Original へ（Staffs には入れない）
			if (line.StartsWith("原作：", StringComparison.Ordinal))
			{
				work.Original = line["原作：".Length..].Trim();
				continue;
			}

			// 3. それ以外 → 「役職：氏名」の形式で分割して Staffs へ
			var parts = line.Split('：', 2);
			staffList.Add(new StaffInfo
			{
				SortOrder = sortOrder++,
				Role = parts.Length == 2 ? parts[0].Trim() : string.Empty,
				Name = parts.Length == 2 ? parts[1].Trim() : line.Trim()
			});
		}

		// 制作会社が複数ある場合は「、」で連結
		work.Production = string.Join("、", productions);
		work.Staffs = staffList;
	}

	/// <summary>
	/// キャスト情報をパースし、声優名のリストを生成します。
	/// </summary>
	/// <param name="cast">スクレイピングされたキャスト情報の改行区切りテキスト。</param>
	/// <param name="work">値を設定する対象の <see cref="AnimeWork"/> オブジェクト。</param>
	private static void parseCast(string cast, AnimeWork work)
	{
		var castList = new List<CastInfo>();
		var sortOrder = 1;

		foreach (var line in splitLines(cast))
		{
			if (line.Contains("未発表")) continue;

			// 「キャラ名：声優名」の形式から声優名のみを取得
			var parts = line.Split('：', 2);
			castList.Add(new CastInfo
			{
				SortOrder = sortOrder++,
				// 分割できた場合は右側（声優名）、できない場合は行全体を名前として扱う
				Name = parts.Length == 2 ? parts[1].Trim() : line.Trim()
			});
		}
		work.Casts = castList;
	}

	/// <summary>
	/// 文字列を環境に依存しない改行コードで分割し、空白を除去した配列を返します。
	/// </summary>
	/// <param name="text">分割対象の文字列。</param>
	/// <returns>分割後の文字列配列。</returns>
	private static string[] splitLines(string text) =>
		text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	/// <summary>
	/// アニメイトのスケジュール文字列から放送局情報を解析し、<see cref="AnimeWork"/> に反映します。
	/// 「：」を含む行（日付・ステージ情報）を除外し、局名行のみを抽出します。
	/// </summary>
	/// <param name="rawText">アニメイトスケジュールの生テキスト（改行区切り）。</param>
	/// <param name="work">値を設定する対象の <see cref="AnimeWork"/>。</param>
	private static void applyScheduleBroadcastInfo(string rawText, AnimeWork work)
	{
		var lines = rawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (lines.Length < 2) return;

		// 「：」を含む行は日付・ステージ情報（例：「2nd STAGE：未発表」「1st STAGE：2026年4月～」）として除外
		var stationLine = string.Join("・", lines[1..].Where(l => !l.Contains('：')));
		if (string.IsNullOrWhiteSpace(stationLine)) return;

		// 「・」で分割して各放送局名ごとに末尾語・不要語を除去し、略称を正式名に変換
		var stations = stationLine
			.Split('・', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(s => stationEndRegex.Replace(s, string.Empty).Trim())
			.Select(s => stationNoiseRegex.Replace(s, string.Empty).Trim())
			.Select(s => stationNameMap.TryGetValue(s, out var mapped) ? mapped : s)
			.Where(s => !string.IsNullOrWhiteSpace(s))
			.ToList();

		if (stations.Count == 0) return;

		work.Broadcast = stations[0];
		work.BroadcastText = string.Join(", ", stations[1..]);
	}
}