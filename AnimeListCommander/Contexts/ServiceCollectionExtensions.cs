using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnimeListCommander.Contexts;

/// <summary>
/// <see cref="IServiceCollection"/> への DI 登録拡張メソッドを提供します。
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// <see cref="ApplicationContext"/> をデータベースからロードし、シングルトンとして DI コンテナに登録します。
	/// </summary>
	/// <param name="services">サービスコレクション。</param>
	/// <param name="configuration">アプリケーション設定。</param>
	/// <returns>自身の <see cref="IServiceCollection"/> を返します。</returns>
	public static IServiceCollection AddApplicationContext(this IServiceCollection services, IConfiguration configuration)
	{
		var connectionString = configuration.GetConnectionString("DefaultConnection")!;
		var builder = new ApplicationContextBuilder(connectionString, NullLogger<ApplicationContextBuilder>.Instance);
		var context = builder.BuildAsync().GetAwaiter().GetResult();
		services.AddSingleton(context);
		return services;
	}
}
