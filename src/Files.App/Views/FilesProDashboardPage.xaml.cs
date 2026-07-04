// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Files.App.Views
{
	public sealed partial class FilesProDashboardPage : Page, IDisposable
	{
		public FilesProDashboardViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<FilesProDashboardViewModel>();

		private IShellPage AppInstance { get; set; } = null!;

		public FilesProDashboardPage()
		{
			InitializeComponent();
		}

		protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			if (e.Parameter is not NavigationArguments parameters)
				return;

			AppInstance = parameters.AssociatedTabInstance!;

			AppInstance.InstanceViewModel.IsPageTypeNotHome = true;
			AppInstance.InstanceViewModel.IsPageTypeSearchResults = false;
			AppInstance.InstanceViewModel.IsPageTypeMtpDevice = false;
			AppInstance.InstanceViewModel.IsPageTypeRecycleBin = false;
			AppInstance.InstanceViewModel.IsPageTypeCloudDrive = false;
			AppInstance.InstanceViewModel.IsPageTypeFtp = false;
			AppInstance.InstanceViewModel.IsPageTypeZipFolder = false;
			AppInstance.InstanceViewModel.IsPageTypeLibrary = false;
			AppInstance.InstanceViewModel.GitRepositoryPath = null;
			AppInstance.InstanceViewModel.IsGitRepository = false;
			AppInstance.InstanceViewModel.IsPageTypeReleaseNotes = false;
			AppInstance.InstanceViewModel.IsPageTypeSettings = false;
			AppInstance.InstanceViewModel.IsPageTypeFilesPro = true;
			AppInstance.ToolbarViewModel.CanRefresh = false;
			AppInstance.ToolbarViewModel.CanGoBack = AppInstance.CanNavigateBackward;
			AppInstance.ToolbarViewModel.CanGoForward = AppInstance.CanNavigateForward;
			AppInstance.ToolbarViewModel.CanNavigateToParent = false;

			AppInstance.SlimContentPage?.StatusBarViewModel.UpdateGitInfo(false, string.Empty, null);
			AppInstance.SlimContentPage?.InfoPaneViewModel.UpdateSelectedItemPreviewAsync();

			AppInstance.ToolbarViewModel.PathComponents.Clear();
			AppInstance.ToolbarViewModel.PathComponents.Add(new PathBoxItem()
			{
				Title = "Files Pro",
				Path = "FilesPro",
				ChevronToolTip = string.Format(Strings.BreadcrumbBarChevronButtonToolTip.GetLocalizedResource(), "Files Pro"),
			});

			base.OnNavigatedTo(e);
		}

		private async void OpenPath_Click(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement { Tag: string path })
				await ViewModel.OpenPathCommand.ExecuteAsync(path);
		}

		private void CopyPath_Click(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement { Tag: string path })
				ViewModel.CopyPathCommand.Execute(path);
		}

		protected override void OnNavigatedFrom(NavigationEventArgs e)
		{
			Dispose();
		}

		public void Dispose()
		{
			ViewModel.Dispose();
		}
	}
}
