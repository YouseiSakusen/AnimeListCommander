using System.Text;
using System.Text.RegularExpressions;
using HalationGhost.Utilities;

namespace AnimeListCommander.Helpers;

/// <summary>
/// アニメタイトルの正規化ユーティリティです。
/// </summary>
public static class AnimeTitleNormalizer
{
	/// <summary>
	/// タイトル中の括弧類（全角・半角・隅付き・鉤括弧等）とその内容を除去するための正規表現。
	/// </summary>
	private static readonly Regex bracketsRegex =
		new(@"[（\(\[【『「].+?[）\)\]】』」]", RegexOptions.Compiled);

	/// <summary>
	/// シーズン・期情報（Season n・シーズンn・第n期・第二期・第nクール等）および
	/// 自立・末尾のローマ数字英字（ii〜xii）を除去するための正規表現。
	/// 漢数字（一〜万）にも対応し、日本語文字境界では \b の代わりに lookaround を使用します。
	/// 作品本体名のみを抽出し、サイト間の表記差を無効化します。
	/// </summary>
	private static readonly Regex seasonNoiseRegex =
		new(@"(?:\d+(?:nd|rd|th|st))?\s*season\s*\d*|season\d+|シーズン\d+|第?(?:\d+|[〇一二三四五六七八九十百千万]+)(?:期|クール|シリーズ)|(?<![a-z])(?:x(?:ii|i)|vi{0,3}|i{1,3}v?|iv|ix)(?![a-z])",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

	/// <summary>
	/// ハイフン <c>-</c> や括弧類（<c>()</c>・<c>[]</c>・<c>【】</c>等）で囲まれた副題・読み仮名・追記を
	/// 囲い文字ごと削除するための正規表現。
	/// </summary>
	private static readonly Regex extraNoiseRegex =
		new(@"-[^-]+-|(?:（[^）]+）|《[^》]+》|〈[^〉]+〉)",
			RegexOptions.Compiled);

	/// <summary>
	/// 全角・半角記号を除去するための正規表現。
	/// </summary>
	private static readonly Regex symbolsRegex =
		new(@"[-!！?？:：;；－_＿~～…]|\.{3}", RegexOptions.Compiled);

	/// <summary>
	/// Unicode ローマ数字と対応するラテン小文字のマッピング。
	/// </summary>
	private static readonly IReadOnlyDictionary<char, string> romanNumeralMap =
		new Dictionary<char, string>
		{
			{ 'Ⅻ', "xii" }, { 'Ⅺ', "xi" }, { 'Ⅹ', "x" },
			{ 'Ⅸ', "ix" }, { 'Ⅷ', "viii" }, { 'Ⅶ', "vii" },
			{ 'Ⅵ', "vi" }, { 'Ⅴ', "v" }, { 'Ⅳ', "iv" },
			{ 'Ⅲ', "iii" }, { 'Ⅱ', "ii" }, { 'Ⅰ', "i" },
			{ 'ⅻ', "xii" }, { 'ⅺ', "xi" }, { 'ⅹ', "x" },
			{ 'ⅸ', "ix" }, { 'ⅷ', "viii" }, { 'ⅶ', "vii" },
			{ 'ⅵ', "vi" }, { 'ⅴ', "v" }, { 'ⅳ', "iv" },
			{ 'ⅲ', "iii" }, { 'ⅱ', "ii" }, { 'ⅰ', "i" },
		};

	/// <summary>
	/// 突合（マッチング）用にタイトルを正規化します。
	/// 括弧とその内容・記号を除去し、英数字を半角化・小文字化します。
	/// </summary>
	/// <param name="input">正規化対象のタイトル文字列。</param>
	/// <returns>正規化済みのタイトル文字列。</returns>
	public static string Normalize(string input)
	{
		if (string.IsNullOrWhiteSpace(input)) return string.Empty;

		var text = input.Normalize(NormalizationForm.FormC);
		text = extraNoiseRegex.Replace(text, "");
		text = bracketsRegex.Replace(text, "");
		text = replaceRomanNumerals(text);
		text = symbolsRegex.Replace(text, "");
		text = JpStringConverter.ToHalfWidthAlphanumeric(text);
		text = text.Replace(" ", "").Replace("　", "");
		text = text.ToLower();
		text = seasonNoiseRegex.Replace(text, "");
		if (text.StartsWith("tvsp")) text = text[4..];
		return text.Trim();
	}

	/// <summary>
	/// 正規化済みタイトル同士が前方一致するかどうかを判定します。
	/// いずれか一方が他方の接頭辞になっている場合に true を返します。
	/// 誤脆防止のため、いずれかのタイトルが 2 文字未満の場合は false を返します。
	/// </summary>
	/// <param name="normalizedA">正規化済みタイトルA。</param>
	/// <param name="normalizedB">正規化済みタイトルB。</param>
	/// <returns>前方一致する場合は true。</returns>
	public static bool IsMatch(string normalizedA, string normalizedB)
	{
		if (normalizedA.Length < 2 || normalizedB.Length < 2) return false;
		return normalizedA.Contains(normalizedB, StringComparison.Ordinal)
			|| normalizedB.Contains(normalizedA, StringComparison.Ordinal);
	}

	/// <summary>
	/// Unicode ローマ数字をラテン小文字アルファベットに置換します。
	/// </summary>
	/// <param name="input">変換対象の文字列。</param>
	/// <returns>ローマ数字を置換した文字列。</returns>
	private static string replaceRomanNumerals(string input)
	{
		if (!input.Any(c => romanNumeralMap.ContainsKey(c))) return input;
		var sb = new StringBuilder(input.Length);
		foreach (var c in input)
		{
			if (romanNumeralMap.TryGetValue(c, out var r))
				sb.Append(r);
			else
				sb.Append(c);
		}
		return sb.ToString();
	}

	/// <summary>
	/// 画像用タイトルとして、英数字のみを半角化した文字列を返します。
	/// </summary>
	/// <param name="input">変換対象のタイトル文字列。</param>
	/// <returns>英数字のみ半角化済みのタイトル文字列。</returns>
	public static string ToImageTitle(string input)
	{
		if (string.IsNullOrWhiteSpace(input)) return string.Empty;
		return JpStringConverter.ToHalfWidthAlphanumeric(input);
	}

	/// <summary>
	/// Windows ファイルシステム禁止文字（\ / : * ? " &lt; &gt; |）を
	/// 対応する全角文字（￥ ／ ： ＊ ？ ＂ ＜ ＞ ｜）に置換し、
	/// 前後の空白および末尾のピリオドを除去した、ディレクトリ名として安全な文字列を返します。
	/// </summary>
	/// <param name="input">変換対象のタイトル文字列。</param>
	/// <returns>ディレクトリ名として安全な文字列。</returns>
	public static string ToSafeDirectoryName(string input)
	{
		if (string.IsNullOrWhiteSpace(input)) return string.Empty;
		return input
			.Replace('\\', '￥')
			.Replace('/', '／')
			.Replace(':', '：')
			.Replace('*', '＊')
			.Replace('?', '？')
			.Replace('"', '＂')
			.Replace('<', '＜')
			.Replace('>', '＞')
			.Replace('|', '｜')
			.Trim()
			.TrimEnd('.');
	}
}
