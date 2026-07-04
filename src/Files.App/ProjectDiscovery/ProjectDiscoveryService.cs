// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Files.App.ProjectDiscovery
{
	internal sealed class ProjectDiscoveryService : IProjectDiscoveryService
	{
		private static readonly string[] ProjectMarkers =
		[
			".git",
			"package.json",
			"pnpm-workspace.yaml",
			"bun.lock",
			"bun.lockb",
			"Cargo.toml",
			"go.mod",
			"pyproject.toml",
			"requirements.txt",
			"wrangler.toml",
			"default.project.json",
			"aftman.toml",
			"rokit.toml",
			"wally.toml",
			"pesde.toml",
			"selene.toml",
			"stylua.toml"
		];

		private static readonly HashSet<string> RobloxExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".lua",
			".luau",
			".rbxl",
			".rbxlx",
			".rbxm",
			".rbxmx"
		};

		private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
		{
			".git",
			".hg",
			".svn",
			".vs",
			"node_modules",
			"bin",
			"obj",
			"target",
			"dist",
			"build",
			".next",
			".nuxt",
			".turbo",
			".cache",
			".venv",
			"__pycache__",
			"Library",
			"Temp"
		};

		private readonly ILogger<ProjectDiscoveryService> logger;

		public ProjectDiscoveryService(ILogger<ProjectDiscoveryService> logger)
		{
			this.logger = logger;
		}

		public Task<ProjectDiscoveryResult> ScanAsync(
			ProjectScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => ScanCore(options, progress, cancellationToken), cancellationToken);
		}

		private ProjectDiscoveryResult ScanCore(
			ProjectScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			var projects = new List<DiscoveredProject>();
			var robloxFiles = new List<RobloxFileInfo>();
			var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var counters = new ScanCounters();

			foreach (var rootPath in options.RootPaths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				ScanDirectory(new DirectoryInfo(rootPath), 0, options, projects, robloxFiles, seenProjects, counters, progress, cancellationToken);

				if (projects.Count >= options.MaxProjects && robloxFiles.Count >= options.MaxRobloxFiles)
					break;
			}

			AttachProjectHints(projects, robloxFiles);

			return new(
				projects
					.OrderByDescending(x => x.LastModified)
					.Take(options.MaxProjects)
					.ToArray(),
				robloxFiles
					.OrderByDescending(x => x.LastModified)
					.Take(options.MaxRobloxFiles)
					.ToArray());
		}

		private void ScanDirectory(
			DirectoryInfo directory,
			int depth,
			ProjectScanOptions options,
			List<DiscoveredProject> projects,
			List<RobloxFileInfo> robloxFiles,
			HashSet<string> seenProjects,
			ScanCounters counters,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (depth > options.MaxDepth)
				return;

			if (ShouldSkipDirectory(directory))
				return;

			counters.DirectoriesVisited++;
			if (counters.DirectoriesVisited % 25 == 0)
				progress?.Report(new("Project index", directory.FullName, counters.DirectoriesVisited, counters.FilesVisited));

			try
			{
				if (projects.Count < options.MaxProjects && IsProjectRoot(directory) && seenProjects.Add(directory.FullName))
					projects.Add(CreateProject(directory));

				if (robloxFiles.Count < options.MaxRobloxFiles)
				{
					foreach (var file in EnumerateFiles(directory))
					{
						cancellationToken.ThrowIfCancellationRequested();
						counters.FilesVisited++;

						if (RobloxExtensions.Contains(file.Extension))
							robloxFiles.Add(CreateRobloxFile(file));

						if (robloxFiles.Count >= options.MaxRobloxFiles)
							break;
					}
				}

				if (projects.Count >= options.MaxProjects && robloxFiles.Count >= options.MaxRobloxFiles)
					return;

				foreach (var child in EnumerateDirectories(directory))
				{
					ScanDirectory(child, depth + 1, options, projects, robloxFiles, seenProjects, counters, progress, cancellationToken);

					if (projects.Count >= options.MaxProjects && robloxFiles.Count >= options.MaxRobloxFiles)
						return;
				}
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
			{
				logger.LogDebug(ex, "Files Pro project scan skipped {Path}", directory.FullName);
			}
		}

		private static bool ShouldSkipDirectory(DirectoryInfo directory)
		{
			if (SkipDirectoryNames.Contains(directory.Name))
				return true;

			try
			{
				return directory.Attributes.HasFlag(FileAttributes.System) &&
					!directory.Attributes.HasFlag(FileAttributes.Directory);
			}
			catch
			{
				return true;
			}
		}

		private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo directory)
		{
			try
			{
				return directory.EnumerateFiles();
			}
			catch
			{
				return [];
			}
		}

		private static IEnumerable<DirectoryInfo> EnumerateDirectories(DirectoryInfo directory)
		{
			try
			{
				return directory.EnumerateDirectories();
			}
			catch
			{
				return [];
			}
		}

		private static bool IsProjectRoot(DirectoryInfo directory)
		{
			foreach (var marker in ProjectMarkers)
			{
				var path = Path.Combine(directory.FullName, marker);
				if (Directory.Exists(path) || File.Exists(path))
					return true;
			}

			return false;
		}

		private static DiscoveredProject CreateProject(DirectoryInfo directory)
		{
			var projectType = DetectProjectType(directory.FullName);
			var git = GetGitInfo(directory.FullName);

			return new()
			{
				Name = directory.Name,
				Path = directory.FullName,
				ProjectType = projectType,
				LastModified = new DateTimeOffset(directory.LastWriteTime),
				GitBranch = git.Branch,
				IsDirty = git.IsDirty,
				RemoteUrl = git.RemoteUrl,
				PackageManager = DetectPackageManager(directory.FullName),
				ScriptsSummary = ReadPackageScripts(directory.FullName)
			};
		}

		private static RobloxFileInfo CreateRobloxFile(FileInfo file)
		{
			return new()
			{
				Name = file.Name,
				Path = file.FullName,
				Extension = file.Extension,
				SizeBytes = (ulong)Math.Max(0L, file.Length),
				LastModified = new DateTimeOffset(file.LastWriteTime),
				ScriptRole = GuessScriptRole(file.FullName)
			};
		}

		private static string DetectProjectType(string path)
		{
			if (HasAny(path, "default.project.json", "aftman.toml", "rokit.toml", "wally.toml", "pesde.toml", "selene.toml", "stylua.toml"))
				return "Roblox";
			if (File.Exists(Path.Combine(path, "wrangler.toml")))
				return "Cloudflare";
			if (File.Exists(Path.Combine(path, "package.json")))
				return "Web";
			if (File.Exists(Path.Combine(path, "Cargo.toml")))
				return "Rust";
			if (File.Exists(Path.Combine(path, "go.mod")))
				return "Go";
			if (HasAny(path, "pyproject.toml", "requirements.txt"))
				return "Python";

			return "Code";
		}

		private static bool HasAny(string path, params string[] names)
			=> names.Any(name => File.Exists(Path.Combine(path, name)) || Directory.Exists(Path.Combine(path, name)));

		private static string DetectPackageManager(string path)
		{
			if (File.Exists(Path.Combine(path, "pnpm-workspace.yaml")) || File.Exists(Path.Combine(path, "pnpm-lock.yaml")))
				return "pnpm";
			if (File.Exists(Path.Combine(path, "bun.lock")) || File.Exists(Path.Combine(path, "bun.lockb")))
				return "bun";
			if (File.Exists(Path.Combine(path, "yarn.lock")))
				return "yarn";
			if (File.Exists(Path.Combine(path, "package-lock.json")))
				return "npm";
			if (File.Exists(Path.Combine(path, "package.json")))
				return "npm";

			return string.Empty;
		}

		private static string ReadPackageScripts(string path)
		{
			var packageJsonPath = Path.Combine(path, "package.json");
			if (!File.Exists(packageJsonPath))
				return string.Empty;

			try
			{
				using var stream = File.OpenRead(packageJsonPath);
				using var document = JsonDocument.Parse(stream);
				if (!document.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
					return string.Empty;

				return string.Join(", ", scripts.EnumerateObject().Select(x => x.Name).Take(8));
			}
			catch
			{
				return string.Empty;
			}
		}

		private static (string Branch, bool IsDirty, string RemoteUrl) GetGitInfo(string path)
		{
			try
			{
				var repoPath = Repository.Discover(path);
				if (string.IsNullOrWhiteSpace(repoPath))
					return (string.Empty, false, string.Empty);

				using var repository = new Repository(repoPath);
				var status = repository.RetrieveStatus(new StatusOptions());

				return (
					repository.Head?.FriendlyName ?? string.Empty,
					status.IsDirty,
					repository.Network.Remotes["origin"]?.Url ?? string.Empty);
			}
			catch
			{
				return (string.Empty, false, string.Empty);
			}
		}

		private static string GuessScriptRole(string path)
		{
			var normalized = path.Replace('\\', '/');
			if (normalized.Contains("/Server", StringComparison.OrdinalIgnoreCase) ||
				normalized.Contains("/ServerScriptService", StringComparison.OrdinalIgnoreCase))
			{
				return "Server";
			}

			if (normalized.Contains("/Client", StringComparison.OrdinalIgnoreCase) ||
				normalized.Contains("/StarterPlayer", StringComparison.OrdinalIgnoreCase) ||
				normalized.Contains("/StarterGui", StringComparison.OrdinalIgnoreCase))
			{
				return "Client";
			}

			if (normalized.Contains("/Shared", StringComparison.OrdinalIgnoreCase) ||
				normalized.Contains("/ReplicatedStorage", StringComparison.OrdinalIgnoreCase))
			{
				return "Shared";
			}

			if (Path.GetFileNameWithoutExtension(path).Contains("module", StringComparison.OrdinalIgnoreCase))
				return "Module";

			return "Unknown";
		}

		private static void AttachProjectHints(List<DiscoveredProject> projects, List<RobloxFileInfo> robloxFiles)
		{
			foreach (var file in robloxFiles)
			{
				var project = projects
					.Where(project => file.Path.StartsWith(project.Path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					.OrderByDescending(project => project.Path.Length)
					.FirstOrDefault();

				file.LikelyProject = project?.Name ?? string.Empty;
			}
		}

		private sealed class ScanCounters
		{
			public int DirectoriesVisited { get; set; }

			public int FilesVisited { get; set; }
		}
	}
}
