using AnimeListCommander.Contexts;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using ZLogger;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// Annict API からアニメ作品情報を取得するサービスです。
/// </summary>
public class AnnictService
{
	/// <summary>
	/// HTTP クライアント。
	/// </summary>
	private readonly HttpClient httpClient;

	/// <summary>
	/// アプリケーションコンテキスト。サービス設定の参照に使用します。
	/// </summary>
	private readonly ApplicationContext applicationContext;

	/// <summary>
	/// ロガー。
	/// </summary>
	private readonly ILogger<AnnictService> logger;

	/// <summary>
	/// <see cref="AnnictService"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="httpClient">HTTP クライアント。</param>
	/// <param name="applicationContext">アプリケーションコンテキスト。</param>
	/// <param name="logger">ロガー。</param>
	public AnnictService(
		HttpClient httpClient,
		ApplicationContext applicationContext,
		ILogger<AnnictService> logger)
	{
		this.httpClient = httpClient;
		this.applicationContext = applicationContext;
		this.logger = logger;
	}

	/// <summary>
	/// 指定クールの Annict アニメ作品一覧をページネーションで全件取得し、TV 作品のみ非同期に返します。
	/// HTTP エラーが発生した場合はログを出力し、空リストを返します。
	/// </summary>
	/// <param name="season">取得対象のクール。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>TV 作品のみを含む <see cref="AnnictWorkDto"/> のリスト。HTTP エラー時は空リスト。</returns>
	public async Task<List<AnnictWorkDto>> GetSeasonAnimeWorksAsync(Season season, CancellationToken ct = default)
	{
		var config = this.applicationContext.ServiceConfigurations
			.Single(c => c.SiteId == (int)ScrapingSite.Annict);

		this.logger.ZLogInformation($"Annict API 接続開始: {season.AnnictTermKey}");

		var allWorks = new List<AnnictWorkDto>();
		var page = 1;

		while (true)
		{
			ct.ThrowIfCancellationRequested();
			this.logger.ZLogInformation($"Annict API 取得中: Page {page}");

			var url = $"{config.BaseUrl}works?access_token={config.ApiKey}&filter_season={season.AnnictTermKey}&per_page=50&page={page}";
			var response = await this.httpClient.GetAsync(url, ct);

			try
			{
				response.EnsureSuccessStatusCode();
			}
			catch (HttpRequestException ex)
			{
				this.logger.ZLogError(ex, $"Annict API の呼び出しに失敗しました: {season.AnnictTermKey} Page {page}");
				return new List<AnnictWorkDto>();
			}

			var pageData = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(ct);
			if (pageData is null)
				break;

			// works 配列を取得して AnnictWorkDto にマッピング
			if (pageData.TryGetValue("works", out var worksObj) && worksObj is JsonElement worksElement)
			{
				foreach (var work in worksElement.EnumerateArray())
				{
					allWorks.Add(new AnnictWorkDto
					{
						AnnictId = work.GetProperty("id").GetInt32(),
						Title = work.GetProperty("title").GetString() ?? string.Empty,
						TitleEn = work.GetProperty("title_en").GetString() ?? string.Empty,
						TitleKana = work.GetProperty("title_kana").GetString() ?? string.Empty,
						WikiUrl = work.GetProperty("wikipedia_url").GetString() ?? string.Empty,
						Media = work.GetProperty("media").GetString() ?? string.Empty,
					});
				}
			}

			// next_page が null であれば終了
			if (!pageData.TryGetValue("next_page", out var nextPageObj)
				|| nextPageObj is not JsonElement nextPageElement
				|| nextPageElement.ValueKind == JsonValueKind.Null)
			{
				break;
			}

			page = nextPageElement.GetInt32();
		}

		// TV 作品のみを返す
		return allWorks.Where(w => w.Media == "tv").ToList();
	}
}
