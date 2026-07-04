// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Indexing
{
	public sealed class EverythingIntegrationStatus
	{
		public bool IsAvailable { get; init; }

		public string ExecutablePath { get; init; } = string.Empty;

		public string Mode { get; init; } = "Not installed";

		public string Reason { get; init; } = "Everything command-line search is optional.";
	}

	public sealed record EverythingSearchQuery(
		string Text,
		int MaxResults = 100);

	public sealed class EverythingSearchResult
	{
		public string Path { get; init; } = string.Empty;

		public string Name => System.IO.Path.GetFileName(Path);
	}
}
