// Copyright (c) Files Community. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Files.App.Utils.SaveDialog;

namespace Files.App.ViewModels
{
	/// <summary>
	/// Backing state for the docked Save bar. The actual file commit (path resolution,
	/// overwrite prompt, writing the result) is performed by the owner (MainPage) via the
	/// CommitRequested / CancelRequested / NewFolderRequested events so it can reach the active
	/// pane and dialog services.
	/// </summary>
	public sealed partial class SaveDialogViewModel : ObservableObject
	{
		[ObservableProperty]
		private bool isActive;

		[ObservableProperty]
		private string fileName = string.Empty;

		[ObservableProperty]
		private IReadOnlyList<FileTypeChoice> fileTypes = Array.Empty<FileTypeChoice>();

		[ObservableProperty]
		private FileTypeChoice? selectedFileType;

		public event EventHandler? CommitRequested;
		public event EventHandler? CancelRequested;
		public event EventHandler? NewFolderRequested;

		public bool CanSave => SaveDialogPathHelper.IsValidFileName(FileName);

		partial void OnFileNameChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

		[RelayCommand(CanExecute = nameof(CanSave))]
		private void Save() => CommitRequested?.Invoke(this, EventArgs.Empty);

		[RelayCommand]
		private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

		[RelayCommand]
		private void NewFolder() => NewFolderRequested?.Invoke(this, EventArgs.Empty);

		/// <summary>Initialize from a parsed request.</summary>
		public void Initialize(Data.Models.SaveDialogRequest request)
		{
			FileTypes = request.FileTypes;
			var idx = Math.Clamp(request.TypeIndex - 1, 0, Math.Max(0, request.FileTypes.Count - 1));
			SelectedFileType = request.FileTypes.Count > 0 ? request.FileTypes[idx] : null;
			FileName = request.SuggestedName;
			IsActive = true;
		}
	}
}
