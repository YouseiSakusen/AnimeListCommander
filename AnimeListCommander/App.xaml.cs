using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using AnimeListCommander.Contexts;
using AnimeListCommander.Intelligences;
using AnimeListCommander.Masters;
using AnimeListCommander.Operations;
using ZLogger;
using ZLogger.Providers;

namespace AnimeListCommander;

/// <summary>
/// アプリケーションのエントリーポイントです。
/// .NET 汎用ホスト（Generic Host）で DI・設定・ロギングを統合します。
/// </summary>
public partial class App
{
	// .NET 汎用ホスト。OnStartup で構築され、アプリケーション終了まで保持されます。
	private static IHost? host;

	/// <summary>
	/// DI サービスプロバイダーを取得します。ホストが未初期化の場合は例外をスローします。
	/// </summary>
	public static IServiceProvider Services => host?.Services ?? throw new InvalidOperationException("ホストが未初期化の状態でアクセスされました。");

	/// <summary>
	/// ZLogger の出力フォーマットを設定します。
	/// <c>yyyy-MM-dd HH:mm:ss.fff [LogLevel] </c> 形式のタイムスタンプをログ行先頭に付与します。
	/// </summary>
	private static void configureZLogger(ZLoggerOptions options) =>
		options.UsePlainTextFormatter(formatter =>
			formatter.SetPrefixFormatter($"{0:yyyy/MM/dd HH:mm:ss.fff} [{1}] ",
				(in MessageTemplate template, in LogInfo info) =>
					template.Format(info.Timestamp.Local, info.LogLevel)));

	/// <summary>
	/// .NET 汎用ホストを構築して返します。
	/// </summary>
	private IHost createHost()
	{
		return Host
			.CreateDefaultBuilder()
			.ConfigureAppConfiguration(c =>
			{
				c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty);
			})
			.ConfigureServices((context, services) =>
			{
				services.AddLogging(logging =>
				{
					logging.AddZLoggerConsole(configureZLogger);
					logging.AddZLoggerRollingFile(options =>
					{
						options.FilePathSelector = (timestamp, sequence) => Path.Combine(AppContext.BaseDirectory, "logs", $"app_{timestamp:yyyyMMdd}_{sequence}.log");
						options.RollingInterval = RollingInterval.Day;
						configureZLogger(options);
					});
				});
				services.AddSingleton<IThemeService, ThemeService>();
				services.AddNavigationWindow<MainWindow, MainWindowViewModel>();
				services.AddNavigationPage<AnimeScrapingPage, AnimeScrapingViewModel>();
				services.AddNavigationPage<AnimeExportPage, AnimeExportViewModel>();
				services.AddApplicationContext(context.Configuration);
				services.AddHttpClient();
				services.AddScoped<IntelligenceRepository>();
				services.AddScoped<AnnictService>();
				services.AddScoped<OfficialPageTitleService>();
				services.AddScoped<ScrapingReporter>();
				services.AddScoped<IntelligenceService>();
				services.AddScoped<MasterRepository>();
				services.AddScoped<OperationsRepository>();
				services.AddScoped<OperationService>();
			})
			.Build();
	}

	/// <summary>
	/// アプリケーション起動時に実行されます。
	/// </summary>
	private async void OnStartup(object sender, StartupEventArgs e)
	{
		host = this.createHost();
		await host.StartAsync();
		host.Services.GetRequiredService<MainWindow>().Show();
	}

	/// <summary>
	/// アプリケーション終了時に実行されます。
	/// </summary>
	private async void OnExit(object sender, ExitEventArgs e)
	{
		await host!.StopAsync();
		host!.Dispose();
	}

	/// <summary>
	/// 未処理例外が発生した際に実行されます。
	/// </summary>
	private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		host!.Services.GetRequiredService<ILogger<App>>().ZLogError(e.Exception, $"未処理例外が発生しました");
		e.Handled = true;
	}
}
