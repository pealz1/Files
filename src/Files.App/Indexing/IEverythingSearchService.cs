// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Indexing
{
	public interface IEverythingSearchService
	{
		Task<EverythingIntegrationStatus> GetStatusAsync(CancellationToken cancellationToken);

		Task<IReadOnlyList<EverythingSearchResult>> SearchAsync(
			EverythingSearchQuery query,
			CancellationToken cancellationToken);
	}
}
