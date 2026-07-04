// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.CommandPalette
{
	public interface ICommandPaletteService
	{
		Task<IReadOnlyList<CommandPaletteItem>> GetCommandsAsync(CancellationToken cancellationToken);

		Task<IReadOnlyList<CommandPaletteItem>> SearchAsync(
			CommandPaletteQuery query,
			CancellationToken cancellationToken);

		Task<CommandPaletteExecutionResult> ExecuteAsync(
			CommandPaletteExecutionRequest request,
			CancellationToken cancellationToken);
	}
}
