using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Serilog;
using Tbot.Exceptions;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Common.Logging.Enrichers;
using TBot.Common.Logging.Hooks;
using TBot.Common.Logging.Sinks;
using TBot.Common.Logging.TextFormatters;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot {

	class Program {
		private static ILoggerService<Program> _logger;
		private static IInstanceManager _settingsHandler;
		static DateTime startTime = DateTime.UtcNow;

		static void Main(string[] args) {
			MainAsync(args).Wait();
		}
		private static void ConfigureSerilog(string logPath, bool telegramLogging) {
			string outTemplate = "[{Timestamp:HH:mm:ss.fff zzz} {ThreadId} {Level:u3} {LogSender}] {Message:lj}{NewLine}{Exception}";
			long maxFileSize = 1 * 1024 * 1024 * 10;
			var loggerConfiguration = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.Enrich.With(new ThreadIdEnricher())
			// Console
			.WriteTo.TBotColoredConsole(
				outputTemplate: outTemplate
			)
			// Log file
			.WriteTo.File(
				path: Path.Combine(logPath, "TBot.log"),
				buffered: false,
				flushToDiskInterval: TimeSpan.FromHours(1),
				rollOnFileSizeLimit: true,
				fileSizeLimitBytes: maxFileSize,
				retainedFileCountLimit: 10,
				rollingInterval: RollingInterval.Day)
			// CSV
			.WriteTo.File(
				path: Path.Combine(logPath, "TBot.csv"),
				buffered: false,
				hooks: new SerilogCSVHeaderHooks(),
				formatter: new SerilogCSVTextFormatter(),
				flushToDiskInterval: TimeSpan.FromHours(1),
				rollOnFileSizeLimit: true,
				fileSizeLimitBytes: maxFileSize,
				rollingInterval: RollingInterval.Day);

			//if (telegramLogging) {
			//	loggerConfiguration.WriteTo.Telegram(botToken: (string) mainSettings.TelegramMessenger.API,
			//		chatId: (string) mainSettings.TelegramMessenger.ChatId,
			//		dateFormat: null,
			//		outputTemplate: "{LogLevelEmoji:l}{LogSenderEmoji:l} {Message:lj}{NewLine}{Exception}");
			//}
			
			Log.Logger = loggerConfiguration.CreateLogger();
		}
		static async Task MainAsync(string[] args) {

			_logger = ServiceProviderFactory.ServiceProvider.GetRequiredService<ILoggerService<Program>>();
			_settingsHandler = ServiceProviderFactory.ServiceProvider.GetRequiredService<IInstanceManager>();
			var ogameService = ServiceProviderFactory.ServiceProvider.GetRequiredService<IOgameService>();
			var helpersService = ServiceProviderFactory.ServiceProvider.GetRequiredService<IHelpersService>();

			helpersService.SetTitle();

			CmdLineArgsService.DoParse(args);
			if (CmdLineArgsService.printHelp) {
				ColoredConsoleWriter.LogToConsole(LogLevel.Information, LogSender.Tbot, $"{System.AppDomain.CurrentDomain.FriendlyName} {CmdLineArgsService.helpStr}");
				Environment.Exit(0);
			}

			if (CmdLineArgsService.settingsPath.IsPresent) {
				_settingsHandler.SettingsAbsoluteFilepath = Path.GetFullPath(CmdLineArgsService.settingsPath.Get());
			}

			var logPath = Path.Combine(Directory.GetCurrentDirectory(), "log");
			if (CmdLineArgsService.logPath.IsPresent == true) {
				logPath = Path.GetFullPath(CmdLineArgsService.logPath.Get());
			}

			ConfigureSerilog(logPath, false);

			// Context validation
			//	a - Ogamed binary is present on same directory ?
			//	b - Settings file does exist ?
			if (!ogameService.ValidatePrerequisites()) {
				Environment.Exit(-1);
			} else if (File.Exists(_settingsHandler.SettingsAbsoluteFilepath) == false) {
				_logger.WriteLog(LogLevel.Error, LogSender.Main, $"\"{_settingsHandler.SettingsAbsoluteFilepath}\" not found. Cannot proceed...");
				Environment.Exit(-1);
			}

			// Manage settings
			_settingsHandler.OnSettingsChanged();

			// Wait for CTRL + C event
			var tcs = new TaskCompletionSource();

			Console.CancelKeyPress += (sender, e) => {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, "CTRL+C pressed!");
				tcs.SetResult();
			};

			await tcs.Task;

			await _settingsHandler.DisposeAsync();
			_logger.WriteLog(LogLevel.Information, LogSender.Main, "Goodbye!");
		}
	}
}
