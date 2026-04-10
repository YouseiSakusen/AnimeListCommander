using AnimeListCommander;
using ZLogger;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="ILogger"/> の統一ログ出力拡張メソッドです。
/// タグ付きフォーマットで一貫したログ出力を提供します。
/// </summary>
public static class LogExtensions
{
	/// <summary>
	/// [INFO] タグを付与して情報ログを出力します。
	/// </summary>
	/// <param name="logger">ロガー。</param>
	/// <param name="message">ログメッセージ。</param>
	public static void ZLogInfo(this ILogger logger, string message)
		=> logger.ZLogInformation($"[{AppConstants.LogTags.Info}] {message}");

	/// <summary>
	/// [***CHECK***] タグを付与して要確認ログを警告レベルで出力します。
	/// </summary>
	/// <param name="logger">ロガー。</param>
	/// <param name="message">ログメッセージ。</param>
	public static void ZLogCheck(this ILogger logger, string message)
		=> logger.ZLogWarning($"[{AppConstants.LogTags.Check}] {message}");

	/// <summary>
	/// [ERROR] タグを付与してエラーログをクリティカルレベルで出力します。
	/// </summary>
	/// <param name="logger">ロガー。</param>
	/// <param name="message">ログメッセージ。</param>
	public static void ZLogError(this ILogger logger, string message)
		=> logger.ZLogCritical($"[{AppConstants.LogTags.Error}] {message}");

	/// <summary>
	/// [ERROR] タグを付与して例外付きエラーログをクリティカルレベルで出力します。
	/// </summary>
	/// <param name="logger">ロガー。</param>
	/// <param name="ex">関連する例外。</param>
	/// <param name="message">ログメッセージ。</param>
	public static void ZLogError(this ILogger logger, Exception? ex, string message)
		=> logger.ZLogCritical(ex, $"[{AppConstants.LogTags.Error}] {message}");
}
