// Copyright (c) Files Community. Licensed under the MIT License.

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Files.App.ViewModels
{
	/// <summary>
	/// Backing state for the docked Open bar shown when the app is launched as an open/upload
	/// dialog. The actual selection is read from the active pane by the owner (MainPage) when the
	/// user clicks Open, so this view model only drives visibility and the Open/Cancel commands.
	/// </summary>
	public sealed partial class OpenDialogViewModel : ObservableObject
	{
		[ObservableProperty]
		private bool isActive;

		public event EventHandler? OpenRequested;
		public event EventHandler? CancelRequested;

		[RelayCommand]
		private void Open() => OpenRequested?.Invoke(this, EventArgs.Empty);

		[RelayCommand]
		private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

		public void Activate() => IsActive = true;
	}
}
