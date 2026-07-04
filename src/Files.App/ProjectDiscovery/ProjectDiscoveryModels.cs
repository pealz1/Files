// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.ProjectDiscovery
{
	public sealed record ProjectScanOptions(
		IReadOnlyList<string> RootPaths,
		int MaxDepth = 8,
		int MaxProjects = 500,
		int MaxRobloxFiles = 1500);

	public sealed record ProjectDiscoveryResult(
		IReadOnlyList<DiscoveredProject> Projects,
		IReadOnlyList<RobloxFileInfo> RobloxFiles);

	public sealed class DiscoveredProject
	{
		public string Name { get; init; } = string.Empty;

		public string Path { get; init; } = string.Empty;

		public string ProjectType { get; init; } = "Code";

		public DateTimeOffset LastModified { get; init; }

		public string LastModifiedText => LastModified.LocalDateTime.ToString("g");

		public string GitBranch { get; init; } = string.Empty;

		public bool IsDirty { get; init; }

		public string GitState => string.IsNullOrWhiteSpace(GitBranch)
			? "No repo"
			: IsDirty ? $"{GitBranch} (dirty)" : $"{GitBranch} (clean)";

		public string RemoteUrl { get; init; } = string.Empty;

		public string PackageManager { get; init; } = string.Empty;

		public string ScriptsSummary { get; init; } = string.Empty;
	}

	public sealed class RobloxFileInfo
	{
		public string Name { get; init; } = string.Empty;

		public string Path { get; init; } = string.Empty;

		public string Extension { get; init; } = string.Empty;

		public ulong SizeBytes { get; init; }

		public string SizeText => SizeBytes.ToSizeString();

		public DateTimeOffset LastModified { get; init; }

		public string LastModifiedText => LastModified.LocalDateTime.ToString("g");

		public string ScriptRole { get; set; } = "Unknown";

		public string LikelyProject { get; set; } = string.Empty;
	}
}
