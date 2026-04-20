using Microsoft.VisualBasic;
using System.Text.RegularExpressions;

namespace HalationGhost.Utilities;

/// <summary>
/// 日本語文字列変換ユーティリティです。
/// </summary>
public static class JpStringConverter
{
	private static readonly Regex fullWidthAlphanumericRegex =
		new(@"[Ａ-Ｚａ-ｚ０-９]", RegexOptions.Compiled);

	// 長い表記を先に評価し、部分マッチを防ぐ順序で列挙する
	// (?<![A-Za-z]) / (?![A-Za-z]) により前後が ASCII 英字でない場合のみ置換する
	private static readonly (Regex pattern, string replacement)[] asciiRomanNumeralMap =
	[
		(new Regex(@"(?<![A-Za-z])XII(?![A-Za-z])",  RegexOptions.Compiled), "Ⅻ"),
		(new Regex(@"(?<![A-Za-z])XI(?![A-Za-z])",   RegexOptions.Compiled), "Ⅺ"),
		(new Regex(@"(?<![A-Za-z])IX(?![A-Za-z])",   RegexOptions.Compiled), "Ⅸ"),
		(new Regex(@"(?<![A-Za-z])VIII(?![A-Za-z])", RegexOptions.Compiled), "Ⅷ"),
		(new Regex(@"(?<![A-Za-z])VII(?![A-Za-z])",  RegexOptions.Compiled), "Ⅶ"),
		(new Regex(@"(?<![A-Za-z])VI(?![A-Za-z])",   RegexOptions.Compiled), "Ⅵ"),
		(new Regex(@"(?<![A-Za-z])IV(?![A-Za-z])",   RegexOptions.Compiled), "Ⅳ"),
		(new Regex(@"(?<![A-Za-z])III(?![A-Za-z])",  RegexOptions.Compiled), "Ⅲ"),
		(new Regex(@"(?<![A-Za-z])II(?![A-Za-z])",   RegexOptions.Compiled), "Ⅱ"),
		(new Regex(@"(?<![A-Za-z])I(?![A-Za-z])",    RegexOptions.Compiled), "Ⅰ"),
		(new Regex(@"(?<![A-Za-z])X(?![A-Za-z])",    RegexOptions.Compiled), "Ⅹ"),
		(new Regex(@"(?<![A-Za-z])V(?![A-Za-z])",    RegexOptions.Compiled), "Ⅴ"),
	];

	/// <summary>
	/// 全角英数字および全角カタカナを半角に変換して返します。
	/// </summary>
	/// <param name="input">変換対象の文字列。</param>
	/// <returns>半角変換後の文字列。</returns>
	public static string ToHalfWidth(string input)
		=> string.IsNullOrEmpty(input) ? string.Empty : Strings.StrConv(input, VbStrConv.Narrow);

	/// <summary>
	/// 文字列内の全角英数字のみを半角に変換して返します。カタカナは維持されます。
	/// </summary>
	public static string ToHalfWidthAlphanumeric(string input)
	{
		if (string.IsNullOrEmpty(input)) return string.Empty;

		// 正規表現でマッチした全角英数字1文字ずつを半角化
		return fullWidthAlphanumericRegex.Replace(input, m =>
			Strings.StrConv(m.Value, VbStrConv.Narrow));
	}

	/// <summary>
	/// 文字列を全角カタカナに変換します（ひらがな→カタカナ、半角→全角）。
	/// </summary>
	/// <param name="input">変換対象の文字列。</param>
	/// <returns>全角カタカナ変換後の文字列。</returns>
	public static string ToFullWidthKatakana(string input)
		=> string.IsNullOrEmpty(input) ? string.Empty : Strings.StrConv(input, VbStrConv.Wide | VbStrConv.Katakana);

	/// <summary>
	/// ASCII ローマ数字（I〜XII）を Unicode のローマ数字文字（Ⅰ〜Ⅻ）に置換します。
	/// 単語境界で区切られた表記のみ対象とし、誤変換を防ぎます。
	/// </summary>
	/// <param name="input">変換対象の文字列。</param>
	/// <returns>ローマ数字置換後の文字列。</returns>
	public static string ReplaceAsciiRomanNumerals(string input)
	{
		if (string.IsNullOrEmpty(input)) return string.Empty;

		var result = input;
		foreach (var (pattern, replacement) in asciiRomanNumeralMap)
			result = pattern.Replace(result, replacement);
		return result;
	}
}
