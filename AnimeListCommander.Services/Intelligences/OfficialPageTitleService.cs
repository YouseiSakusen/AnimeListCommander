using AngleSharp;
using Microsoft.Extensions.Logging;
using System.Text;
using ZLogger;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// アニメ作品の公式サイトから &lt;title&gt; タグの内容を取得するサービスです。
/// </summary>
public class OfficialPageTitleService
{
	private readonly IHttpClientFactory httpClientFactory;
	private readonly ILogger<OfficialPageTitleService> logger;

	/// <summary>
	/// <see cref="OfficialPageTitleService"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="httpClientFactory">HTTP クライアントファクトリー。</param>
	/// <param name="logger">ロガー。</param>
	public OfficialPageTitleService(IHttpClientFactory httpClientFactory, ILogger<OfficialPageTitleService> logger)
	{
		this.httpClientFactory = httpClientFactory;
		this.logger = logger;
	}

	/// <summary>
	/// 作品の公式サイト URL から &lt;title&gt; タグの内容を取得し、<see cref="AnimeWork.OfficialPageTitle"/> に設定します。
	/// URL が空または無効な場合、および HTTP エラーが発生した場合は処理をスキップして次の作品へ進みます。
	/// </summary>
	/// <param name="work">タイトル取得対象のアニメ作品。</param>
	/// <param name="ct">キャンセルトークン。</param>
	public async Task SetOfficialPageTitleAsync(AnimeWork work, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(work.OfficialSiteUrl))
			return;

		if (!Uri.TryCreate(work.OfficialSiteUrl, UriKind.Absolute, out var uri) ||
			(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			return;

		var httpClient = this.httpClientFactory.CreateClient(nameof(OfficialPageTitleService));

		HttpResponseMessage response;
		try
		{
			var request = new HttpRequestMessage(HttpMethod.Get, work.OfficialSiteUrl);
			request.Headers.UserAgent.ParseAdd(
				"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
			response = await httpClient.SendAsync(request, ct);
		}
		catch (HttpRequestException ex)
		{
			this.logger.ZLogWarning(ex, $"「{work.Title}」の公式サイト取得に失敗しました: {work.OfficialSiteUrl}");
			return;
		}
		catch (TaskCanceledException ex)
		{
			this.logger.ZLogWarning(ex, $"「{work.Title}」の公式サイト取得がタイムアウトしました: {work.OfficialSiteUrl}");
			return;
		}

		if (!response.IsSuccessStatusCode)
		{
			this.logger.ZLogWarning($"「{work.Title}」の公式サイト取得が失敗しました: HTTP {(int)response.StatusCode} ({work.OfficialSiteUrl})");
			return;
		}

		byte[] bytes;
		try
		{
			bytes = await response.Content.ReadAsByteArrayAsync(ct);
		}
		catch (Exception ex)
		{
			this.logger.ZLogWarning(ex, $"「{work.Title}」の公式サイトコンテンツ読み取りに失敗しました: {work.OfficialSiteUrl}");
			return;
		}

		var charset = response.Content.Headers.ContentType?.CharSet;
		Encoding encoding;
		try
		{
			encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
		}
		catch (ArgumentException)
		{
			encoding = Encoding.UTF8;
		}

		var html = encoding.GetString(bytes);
		var config = Configuration.Default.WithDefaultLoader();
		using var context = BrowsingContext.New(config);
		var document = await context.OpenAsync(req => req.Content(html));

		var title = document.Title ?? string.Empty;
		work.OfficialPageTitle = title;
		this.logger.ZLogInfo($"「{work.Title}」の公式サイトタイトルを取得しました: {title}");
	}
}
