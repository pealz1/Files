// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;

namespace Files.App.ProjectDiscovery
{
	public interface IProjectDiscoveryService
	{
		Task<ProjectDiscoveryResult> ScanAsync(
			ProjectScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);
	}
}
