// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.CommandPalette
{
	public sealed record CommandPaletteQuery(
		string Text,
		int MaxResults = 50);

	public sealed class CommandPaletteItem
	{
		public string Id { get; init; } = string.Empty;

		public string Title { get; init; } = string.Empty;

		public string Subtitle { get; init; } = string.Empty;

		public string Category { get; init; } = string.Empty;

		public string Glyph { get; init; } = "\uE8B7";

		public bool RequiresConfirmation { get; init; }

		public string SafetyText => RequiresConfirmation ? "Confirm" : "Ready";
	}

	public sealed record CommandPaletteExecutionRequest(
		string CommandId,
		string WorkingDirectory,
		string? ShellCommand,
		bool Confirmed);

	public sealed class CommandPaletteExecutionResult
	{
		public bool Succeeded { get; init; }

		public string Message { get; init; } = string.Empty;

		public string? TargetView { get; init; }
	}
}
