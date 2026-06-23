// Copyright (c) Files Community. Licensed under the MIT License.

namespace Files.App.Utils.SaveDialog
{
	/// <summary>
	/// One entry in the Save dialog "Save as type" dropdown.
	/// </summary>
	/// <param name="Display">Friendly name shown to the user, e.g. "PNG Image".</param>
	/// <param name="Pattern">Raw filter pattern, e.g. "*.png" or "*.jpg;*.jpeg" or "*.*".</param>
	/// <param name="PrimaryExtension">First concrete extension incl. dot (".png"), or "" for *.* .</param>
	public sealed record FileTypeChoice(string Display, string Pattern, string PrimaryExtension)
	{
		public override string ToString() => Display;
	}
}
