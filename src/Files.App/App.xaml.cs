// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Windows.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Files.App
{
	/// <summary>
	/// Represents the entry point of UI for Files app.
	/// </summary>
	public partial class App : Application
	{
		public static SystemTrayIcon? SystemTrayIcon { get; private set; }

		public static TaskCompletionSource? SplashScreenLoadingTCS { get; private set; }
		public static string? OutputPath { get; set; }
		public static bool IsSaveDialog { get; set; }
		public static bool IsOpenDialog { get; set; }
		public static bool IsPickFolders { get; set; }
		public static Data.Models.SaveDialogRequest? SaveDialogRequest { get; set; }

		/// <summary>True once a dialog has committed (Save/Open clicked). Closing without this is a cancel.</summary>
		public static bool DialogCommitted { get; set; }

		/// <summary>
		/// Name of the per-dialog completion event passed via -doneevent. Each dialog uses a unique
		/// event so concurrent dialogs never wake each other (which previously crashed the host).
		/// </summary>
		public static string? DoneEventName { get; set; }

		/// <summary>Open dialog: commit the current file selection (set by MainPage). Invoked when the user
		/// double-clicks/opens a file so it commits to the dialog instead of launching it in another app.</summary>
		public static Action? OpenDialogCommitSelection { get; set; }

		/// <summary>Save dialog: put a double-clicked file's name into the File name box (set by MainPage).</summary>
		public static Action<string>? SaveDialogFillFileName { get; set; }

		/// <summary>Signal the native dialog that the app finished (commit or cancel).</summary>
		public static void SignalDialogDone()
		{
			var name = string.IsNullOrEmpty(DoneEventName) ? "FILEDIALOG" : DoneEventName;
			using var handle = PInvoke.CreateEvent(null, false, false, name);
			PInvoke.SetEvent(handle);
		}

		private static CommandBarFlyout? _LastOpenedFlyout;
		public static CommandBarFlyout? LastOpenedFlyout
		{
			set
			{
				_LastOpenedFlyout = value;

				if (_LastOpenedFlyout is not null)
					_LastOpenedFlyout.Closed += LastOpenedFlyout_Closed;
			}
		}

		// TODO: Replace with DI
		public static QuickAccessManager QuickAccessManager { get; private set; } = null!;
		public static StorageHistoryWrapper HistoryWrapper { get; private set; } = null!;
		public static FileTagsManager FileTagsManager { get; private set; } = null!;
		public static LibraryManager LibraryManager { get; private set; } = null!;
		public static AppModel AppModel { get; private set; } = null!;
		public static ILogger Logger { get; private set; } = NullLogger.Instance;

		/// <summary>
		/// Initializes an instance of <see cref="App"/>.
		/// </summary>
		public App()
		{
			InitializeComponent();

			// Configure exception handlers
			UnhandledException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.Exception, true);
			AppDomain.CurrentDomain.UnhandledException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.ExceptionObject as Exception, false);
			TaskScheduler.UnobservedTaskException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.Exception, false);
		}

		/// <summary>
		/// Gets invoked when the application is launched normally by the end user.
		/// </summary>
		protected override void OnLaunched(LaunchActivatedEventArgs e)
		{
			_ = ActivateAsync().ContinueWith(task =>
			{
				AppLifecycleHelper.HandleAppUnhandledException(task.Exception, true);
			}, TaskContinuationOptions.OnlyOnFaulted);

			async Task ActivateAsync()
			{
				// Get AppActivationArguments
				var appActivationArguments = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
				var isStartupTask = appActivationArguments.Data is Windows.ApplicationModel.Activation.IStartupTaskActivatedEventArgs;

				if (!isStartupTask)
				{
					// Initialize and activate MainWindow
					MainWindow.Instance.Activate();

					// Wait for the Window to initialize
					await Task.Delay(10);

					SplashScreenLoadingTCS = new TaskCompletionSource();
					MainWindow.Instance.ShowSplashScreen();
				}

				// Configure the DI (dependency injection) container
				var host = AppLifecycleHelper.ConfigureHost();
				Ioc.Default.ConfigureServices(host.Services);

				// Configure Sentry
				if (AppLifecycleHelper.AppEnvironment is not AppEnvironment.Dev)
					AppLifecycleHelper.ConfigureSentry();

				var userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
				var isLeaveAppRunning = userSettingsService.GeneralSettingsService.LeaveAppRunning;

				if (isStartupTask && !isLeaveAppRunning)
				{
					// Initialize and activate MainWindow
					MainWindow.Instance.Activate();

					// Wait for the Window to initialize
					await Task.Delay(10);

					SplashScreenLoadingTCS = new TaskCompletionSource();
					MainWindow.Instance.ShowSplashScreen();
				}

				// TODO: Replace with DI
				QuickAccessManager = Ioc.Default.GetRequiredService<QuickAccessManager>();
				HistoryWrapper = Ioc.Default.GetRequiredService<StorageHistoryWrapper>();
				FileTagsManager = Ioc.Default.GetRequiredService<FileTagsManager>();
				LibraryManager = Ioc.Default.GetRequiredService<LibraryManager>();
				Logger = Ioc.Default.GetRequiredService<ILogger<App>>();
				AppModel = Ioc.Default.GetRequiredService<AppModel>();

				// Hook events for the window
				MainWindow.Instance.Closed += Window_Closed;
				MainWindow.Instance.Activated += Window_Activated;

				Logger.LogInformation($"App launched. Launch args type: {appActivationArguments.Data.GetType().Name}");

				if (!(isStartupTask && isLeaveAppRunning))
				{
					// Wait for the UI to update
					await SplashScreenLoadingTCS!.Task.WithTimeoutAsync(TimeSpan.FromMilliseconds(500));
					SplashScreenLoadingTCS = null;

					// Create a system tray icon
					SystemTrayIcon = new SystemTrayIcon();
					if (userSettingsService.GeneralSettingsService.ShowSystemTrayIcon)
						SystemTrayIcon.Show();

					Logger.LogInformation("Splash screen ready or timed out; initializing main window.");
					_ = MainWindow.Instance.InitializeApplicationAsync(appActivationArguments.Data).ContinueWith(task =>
					{
						AppLifecycleHelper.HandleAppUnhandledException(task.Exception, true);
					}, TaskContinuationOptions.OnlyOnFaulted);
				}
				else
				{
					// Create a system tray icon
					SystemTrayIcon = new SystemTrayIcon();
					if (userSettingsService.GeneralSettingsService.ShowSystemTrayIcon)
						SystemTrayIcon.Show();

					// Sleep current instance
					Program.Pool = new(0, 1, $"Files-{AppLifecycleHelper.AppEnvironment}-Instance");

					Thread.Yield();

					if (Program.Pool.WaitOne())
					{
						// Resume the instance
						Program.Pool.Dispose();
						Program.Pool = null;
					}
				}

				Logger.LogInformation("Initializing background app components.");
				await AppLifecycleHelper.InitializeAppComponentsAsync();
				Logger.LogInformation("Background app components initialized.");
			}
		}

		/// <summary>
		/// Gets invoked when the application is activated.
		/// </summary>
		public async Task OnActivatedAsync(AppActivationArguments activatedEventArgs)
		{
			var activatedEventArgsData = activatedEventArgs.Data;

			Logger.LogInformation($"The app is being activated. Activation type: {activatedEventArgsData?.GetType().Name ?? "Unknown"}");

			// InitializeApplication accesses UI, needs to be called on UI thread
			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(()
				=> MainWindow.Instance.InitializeApplicationAsync(activatedEventArgsData));
		}

		/// <summary>
		/// Gets invoked when the main window is activated.
		/// </summary>
		private void Window_Activated(object sender, WindowActivatedEventArgs args)
		{
			Logger.LogInformation($"Window_Activated: State={args?.WindowActivationState.ToString()}");

			if (args.WindowActivationState != WindowActivationState.Deactivated)
				AppModel.IsMainWindowClosed = false;

			// TODO(s): Is this code still needed?
			if (args.WindowActivationState != WindowActivationState.CodeActivated ||
				args.WindowActivationState != WindowActivationState.PointerActivated)
				return;

			ApplicationData.Current.LocalSettings.Values["INSTANCE_ACTIVE"] = -Environment.ProcessId;
		}

		/// <summary>
		/// Gets invoked when the application execution is closed.
		/// </summary>
		/// <remarks>
		/// Saves the current state of the app such as opened tabs, and disposes all cached resources.
		/// </remarks>
		private async void Window_Closed(object sender, WindowEventArgs args)
		{
			// Save application state and stop any background activity
			IUserSettingsService userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
			StatusCenterViewModel statusCenterViewModel = Ioc.Default.GetRequiredService<StatusCenterViewModel>();
			ICommandManager commandManager = Ioc.Default.GetRequiredService<ICommandManager>();

			// A Workaround for the crash (#10110)
			if (_LastOpenedFlyout?.IsOpen ?? false)
			{
				args.Handled = true;
				_LastOpenedFlyout.Closed += (sender, e) => App.Current.Exit();
				_LastOpenedFlyout.Hide();
				return;
			}

			// Persist the session only for real browsing windows. A dialog (save/open) shows a
			// single throwaway folder tab; saving it as the session would clobber the user's real
			// tabs, and closing all tabs is pointless for a window that is going away anyway.
			var isDialogWindow = IsSaveDialog || IsOpenDialog || OutputPath is not null;
			if (!isDialogWindow)
			{
				// Save the current tab list in case it was overwriten by another instance
				if (userSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp || userSettingsService.AppSettingsService.RestoreTabsOnStartup)
					AppLifecycleHelper.SaveSessionTabs();
				else
					await commandManager.CloseAllTabs.ExecuteAsync();
			}

			if (OutputPath is not null)
			{
				// Both Save and Open dialogs commit ONLY via their explicit button (Save / Open).
				// Closing the window without committing is a cancel: signal the native side so it
				// returns ERROR_CANCELLED and writes nothing - never saving or uploading something
				// the user did not confirm.
				if (!DialogCommitted)
					SignalDialogDone();
			}

			// Continue running the app on the background
			if (userSettingsService.GeneralSettingsService.LeaveAppRunning &&
				!AppModel.ForceProcessTermination &&
				!Process.GetProcessesByName("Files").Any(x => x.Id != Environment.ProcessId))
			{
				// Close open content dialogs
				UIHelpers.CloseAllDialogs();

				// Close all notification banners except in progress
				statusCenterViewModel.RemoveAllCompletedItems();

				// Cache the window instead of closing it
				MainWindow.Instance.AppWindow.Hide();
				AppModel.IsMainWindowClosed = true;

				// Close all tabs
				MainPageViewModel.AppInstances.ForEach(tabItem => tabItem.Unload());
				MainPageViewModel.AppInstances.Clear();

				// Wait for all properties windows to close
				await FilePropertiesHelpers.WaitClosingAll();

				// Sleep current instance
				Program.Pool = new(0, 1, $"Files-{AppLifecycleHelper.AppEnvironment}-Instance");

				Thread.Yield();

				// Displays a notification the first time the app goes to the background
				if (userSettingsService.AppSettingsService.ShowBackgroundRunningNotification)
				{
					SafetyExtensions.IgnoreExceptions(() =>
					{
						AppToastNotificationHelper.ShowBackgroundRunningToast();

						userSettingsService.AppSettingsService.ShowBackgroundRunningNotification = false;
					});
				}

				if (Program.Pool.WaitOne())
				{
					// Resume the instance
					Program.Pool.Dispose();
					Program.Pool = null;

					if (!AppModel.ForceProcessTermination)
					{
						args.Handled = true;
						_ = AppLifecycleHelper.CheckAppUpdate();
						return;
					}
				}
			}

			// Stop the tray icon's hidden window before continuing teardown so a late "Quit"
			// click can't dispatch into OnQuitClicked once Application.Current is null.
			SystemTrayIcon?.Dispose();
			SystemTrayIcon = null;

			// Method can take a long time, make sure the window is hidden
			await Task.Yield();

			// Try to maintain clipboard data after app close
			SafetyExtensions.IgnoreExceptions(() =>
			{
				var dataPackage = Clipboard.GetContent();
				if (dataPackage.Properties.PackageFamilyName == Package.Current.Id.FamilyName)
				{
					if (dataPackage.Contains(StandardDataFormats.StorageItems))
						Clipboard.Flush();
				}
			},
			Logger);

			// Destroy cached properties windows
			FilePropertiesHelpers.DestroyCachedWindows();
			AppModel.IsMainWindowClosed = true;

			// Wait for ongoing file operations
			FileOperationsHelpers.WaitForCompletion();
		}

		/// <summary>
		/// Gets invoked when the last opened flyout is closed.
		/// </summary>
		private static void LastOpenedFlyout_Closed(object? sender, object e)
		{
			if (sender is not CommandBarFlyout commandBarFlyout)
				return;

			commandBarFlyout.Closed -= LastOpenedFlyout_Closed;
			if (_LastOpenedFlyout == commandBarFlyout)
				_LastOpenedFlyout = null;
		}
	}
}
