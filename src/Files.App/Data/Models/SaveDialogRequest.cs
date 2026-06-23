// Copyright (c) Files Community. Licensed under the MIT License.

using System.Collections.Generic;
using Files.App.Utils.SaveDialog;

namespace Files.App.Data.Models
{
	/// <summary>
	/// Immutable description of a Save dialog invocation, parsed from launch args.
	/// </summary>
	public sealed record SaveDialogRequest(
		string SuggestedName,
		IReadOnlyList<FileTypeChoice> FileTypes,
		int TypeIndex);
}
