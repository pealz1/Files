// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;

namespace Files.App.Indexing
{
	internal sealed class EverythingSearchService : IEverythingSearchService
	{
		private readonly ILogger<EverythingSearchService> logger;

		private EverythingIntegrationStatus? cachedStatus;

		public EverythingSearchService(ILogger<EverythingSearchService> logger)
		{
			this.logger = logger;
		}

		public Task<EverythingIntegrationStatus> GetStatusAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			cachedStatus ??= DetectEverything();
			return Task.FromResult(cachedStatus);
		}

		public async Task<IReadOnlyList<EverythingSearchResult>> SearchAsync(
			EverythingSearchQuery query,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(query.Text))
				return [];

			var status = await GetStatusAsync(cancellationToken);
			if (!status.IsAvailable)
				return [];

			var arguments = $"-n {Math.Clamp(query.MaxResults, 1, 10000)} {Quote(query.Text)}";
			var startInfo = new ProcessStartInfo(status.ExecutablePath, arguments)
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};

			try
			{
				using var process = Process.Start(startInfo);
				if (process is null)
					return [];

				var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
				var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
				await process.WaitForExitAsync(cancellationToken);

				if (process.ExitCode != 0)
				{
					var error = await errorTask;
					logger.LogDebug("Everything search returned {ExitCode}: {Error}", process.ExitCode, error);
					return [];
				}

				var output = await outputTask;
				return output
					.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Where(File.Exists)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.Take(query.MaxResults)
					.Select(path => new EverythingSearchResult { Path = path })
					.ToArray();
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogDebug(ex, "Everything command-line search failed");
				return [];
			}
		}

		private static EverythingIntegrationStatus DetectEverything()
		{
			var candidates = new List<string>();
			var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
			candidates.AddRange(path
				.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(directory => Path.Combine(directory, "es.exe")));

			var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			if (!string.IsNullOrWhiteSpace(programFiles))
				candidates.Add(Path.Combine(programFiles, "Everything", "es.exe"));
			if (!string.IsNullOrWhiteSpace(programFilesX86))
				candidates.Add(Path.Combine(programFilesX86, "Everything", "es.exe"));

			var executable = candidates.FirstOrDefault(File.Exists);
			if (string.IsNullOrWhiteSpace(executable))
			{
				return new()
				{
					IsAvailable = false,
					Mode = "Fallback index",
					Reason = "Everything es.exe was not found. Files Pro will use its SQLite index."
				};
			}

			return new()
			{
				IsAvailable = true,
				ExecutablePath = executable,
				Mode = "Everything CLI",
				Reason = "Everything command-line search is available as an optional accelerator."
			};
		}

		private static string Quote(string value)
			=> "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
	}
}
