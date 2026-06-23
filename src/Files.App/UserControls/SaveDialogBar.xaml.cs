// Copyright (c) Files Community. Licensed under the MIT License.

using Files.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Files.App.UserControls
{
	public sealed partial class SaveDialogBar : UserControl
	{
		private SaveDialogViewModel? ViewModel => DataContext as SaveDialogViewModel;

		public SaveDialogBar()
		{
			InitializeComponent();
		}

		private void FileNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (ViewModel is null)
				return;

			if (e.Key == VirtualKey.Enter && ViewModel.SaveCommand.CanExecute(null))
			{
				ViewModel.SaveCommand.Execute(null);
				e.Handled = true;
			}
			else if (e.Key == VirtualKey.Escape)
			{
				ViewModel.CancelCommand.Execute(null);
				e.Handled = true;
			}
		}

		/// <summary>Focus the file name box and select the stem (everything before the extension).</summary>
		public void FocusFileName()
		{
			FileNameBox.Focus(FocusState.Programmatic);
			FileNameBox.SelectionStart = 0;
			var name = FileNameBox.Text;
			var dot = name.LastIndexOf('.');
			FileNameBox.SelectionLength = dot > 0 ? dot : name.Length;
		}
	}
}
