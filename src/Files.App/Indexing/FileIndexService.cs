// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using Windows.Storage;

namespace Files.App.Indexing
{
	internal sealed class FileIndexService : IFileIndexService
	{
		private const int SchemaVersion = 1;

		private static readonly HashSet<string> DefaultIgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
		{
			"$Recycle.Bin",
			"System Volume Information",
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
			"__pycache__"
		};

		private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".zip",
			".rar",
			".7z",
			".tar",
			".gz",
			".bz2",
			".xz",
			".iso"
		};

		private static readonly HashSet<string> RobloxExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".lua",
			".luau",
			".rbxl",
			".rbxlx",
			".rbxm",
			".rbxmx"
		};

		private readonly ILogger<FileIndexService> logger;

		public FileIndexService(ILogger<FileIndexService> logger)
		{
			this.logger = logger;
		}

		public async Task<FileIndexStats> GetStatsAsync(CancellationToken cancellationToken)
		{
			var databasePath = GetDatabasePath();
			await EnsureDatabaseAsync(databasePath, cancellationToken);

			await using var connection = CreateConnection(databasePath);
			await connection.OpenAsync(cancellationToken);

			var indexedItems = Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM files", cancellationToken) ?? 0);
			var lastScanRaw = await ScalarAsync(connection, "SELECT value FROM metadata WHERE key = 'last_scan_utc'", cancellationToken) as string;
			var lastScan = long.TryParse(lastScanRaw, out var unixSeconds)
				? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
				: null as DateTimeOffset?;

			return new()
			{
				SchemaVersion = SchemaVersion,
				IndexedItems = indexedItems,
				IndexSizeBytes = File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0L,
				LastScanTime = lastScan,
				DatabasePath = databasePath
			};
		}

		public async Task<FileIndexStats> RebuildAsync(
			FileIndexOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			var databasePath = GetDatabasePath();
			await EnsureDatabaseAsync(databasePath, cancellationToken);

			var scanId = Guid.NewGuid().ToString("N");
			var indexed = 0;
			var visitedDirectories = 0;
			var excludedRoots = new HashSet<string>(options.ExcludedRoots ?? [], StringComparer.OrdinalIgnoreCase);
			var ignoredFolders = new HashSet<string>(DefaultIgnoredFolders, StringComparer.OrdinalIgnoreCase);

			foreach (var ignoredFolder in options.IgnoredFolderNames ?? [])
				ignoredFolders.Add(ignoredFolder);

			await using var connection = CreateConnection(databasePath);
			await connection.OpenAsync(cancellationToken);

			await ExecuteAsync(connection, "DELETE FROM files", cancellationToken);
			await ExecuteAsync(connection, "DELETE FROM content", cancellationToken);
			await ExecuteAsync(connection, "INSERT OR REPLACE INTO metadata(key, value) VALUES ('active_scan_id', $value)", cancellationToken, ("$value", scanId));

			await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
			await using var insertFileCommand = connection.CreateCommand();
			insertFileCommand.Transaction = (SqliteTransaction)transaction;
			insertFileCommand.CommandText =
				"""
				INSERT OR REPLACE INTO files
					(path, name, directory_path, extension, size_bytes, last_modified_utc, is_directory, is_archive, is_roblox, is_repo, is_downloads, last_seen_scan_id)
				VALUES
					($path, $name, $directory, $extension, $size, $modified, $isDirectory, $isArchive, $isRoblox, $isRepo, $isDownloads, $scanId)
				""";

			var insertParameters = new[]
			{
				insertFileCommand.Parameters.Add("$path", SqliteType.Text),
				insertFileCommand.Parameters.Add("$name", SqliteType.Text),
				insertFileCommand.Parameters.Add("$directory", SqliteType.Text),
				insertFileCommand.Parameters.Add("$extension", SqliteType.Text),
				insertFileCommand.Parameters.Add("$size", SqliteType.Integer),
				insertFileCommand.Parameters.Add("$modified", SqliteType.Integer),
				insertFileCommand.Parameters.Add("$isDirectory", SqliteType.Integer),
				insertFileCommand.Parameters.Add("$isArchive", SqliteType.Integer),
				insertFileCommand.Parameters.Add("$isRoblox", SqliteType.Integer),
				insertFileCommand.Parameters.Add("$isRepo", SqliteType.Integer),
				insertFileCommand.Parameters.Add("$isDownloads", SqliteType.Integer),
				insertFileCommand.Parameters.Add("$scanId", SqliteType.Text)
			};

			await using var insertContentCommand = connection.CreateCommand();
			insertContentCommand.Transaction = (SqliteTransaction)transaction;
			insertContentCommand.CommandText = "INSERT OR REPLACE INTO content(path, excerpt) VALUES ($path, $excerpt)";
			var contentPathParameter = insertContentCommand.Parameters.Add("$path", SqliteType.Text);
			var contentExcerptParameter = insertContentCommand.Parameters.Add("$excerpt", SqliteType.Text);

			foreach (var root in options.IncludedRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();

				await IndexDirectoryAsync(
					new DirectoryInfo(root),
					depth: 0,
					insideRepository: Directory.Exists(Path.Combine(root, ".git")),
					options,
					excludedRoots,
					ignoredFolders,
					insertFileCommand,
					insertParameters,
					insertContentCommand,
					contentPathParameter,
					contentExcerptParameter,
					scanId,
					progress,
					counters: new IndexCounters(indexed, visitedDirectories),
					countsChanged: value =>
					{
						indexed = value.Files;
						visitedDirectories = value.Directories;
					},
					cancellationToken);

				if (indexed >= options.MaxFiles)
					break;
			}

			await transaction.CommitAsync(cancellationToken);
			await ExecuteAsync(connection, "INSERT OR REPLACE INTO metadata(key, value) VALUES ('last_scan_utc', $value)", cancellationToken, ("$value", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()));
			await ExecuteAsync(connection, "INSERT OR REPLACE INTO metadata(key, value) VALUES ('schema_version', $value)", cancellationToken, ("$value", SchemaVersion.ToString()));

			return await GetStatsAsync(cancellationToken);
		}

		public async Task<IReadOnlyList<FileSearchResult>> SearchAsync(
			FileSearchQuery query,
			CancellationToken cancellationToken)
		{
			var databasePath = GetDatabasePath();
			await EnsureDatabaseAsync(databasePath, cancellationToken);

			var parsed = Parse(query.Text);
			var sql = new StringBuilder(
				"""
				SELECT path, name, directory_path, extension, size_bytes, last_modified_utc, is_archive, is_roblox, is_repo, is_downloads
				FROM files
				WHERE 1 = 1
				""");
			sql.AppendLine();

			var parameters = new List<(string Name, object? Value)>();
			if (!string.IsNullOrWhiteSpace(parsed.Extension))
			{
				sql.AppendLine("AND extension = $extension");
				parameters.Add(("$extension", parsed.Extension.StartsWith('.') ? parsed.Extension : "." + parsed.Extension));
			}

			if (parsed.Repo is bool repo)
			{
				sql.AppendLine("AND is_repo = $repo");
				parameters.Add(("$repo", repo ? 1 : 0));
			}

			if (parsed.Roblox is bool roblox)
			{
				sql.AppendLine("AND is_roblox = $roblox");
				parameters.Add(("$roblox", roblox ? 1 : 0));
			}

			if (parsed.Downloads is bool downloads)
			{
				sql.AppendLine("AND is_downloads = $downloads");
				parameters.Add(("$downloads", downloads ? 1 : 0));
			}

			if (parsed.Archive is bool archive)
			{
				sql.AppendLine("AND is_archive = $archive");
				parameters.Add(("$archive", archive ? 1 : 0));
			}

			if (parsed.MinSizeBytes is long minSizeBytes)
			{
				sql.AppendLine("AND size_bytes >= $minSize");
				parameters.Add(("$minSize", minSizeBytes));
			}

			if (parsed.MaxSizeBytes is long maxSizeBytes)
			{
				sql.AppendLine("AND size_bytes <= $maxSize");
				parameters.Add(("$maxSize", maxSizeBytes));
			}

			if (parsed.ModifiedAfter is DateTimeOffset modifiedAfter)
			{
				sql.AppendLine("AND last_modified_utc >= $modifiedAfter");
				parameters.Add(("$modifiedAfter", modifiedAfter.ToUnixTimeSeconds()));
			}

			if (parsed.Duplicate is true)
				sql.AppendLine("AND size_bytes IN (SELECT size_bytes FROM files WHERE size_bytes > 0 GROUP BY size_bytes HAVING COUNT(*) > 1)");

			if (!string.IsNullOrWhiteSpace(parsed.Term))
			{
				sql.AppendLine("AND (name LIKE $like OR path LIKE $like OR EXISTS (SELECT 1 FROM content WHERE content.path = files.path AND excerpt LIKE $like))");
				parameters.Add(("$like", "%" + parsed.Term + "%"));
			}

			sql.AppendLine("ORDER BY last_modified_utc DESC LIMIT $limit");
			parameters.Add(("$limit", Math.Max(query.MaxResults * 4, query.MaxResults)));

			await using var connection = CreateConnection(databasePath);
			await connection.OpenAsync(cancellationToken);
			await using var command = connection.CreateCommand();
			command.CommandText = sql.ToString();
			foreach (var parameter in parameters)
				command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);

			var candidates = new List<FileSearchResult>();
			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var result = new FileSearchResult
				{
					Path = reader.GetString(0),
					Name = reader.GetString(1),
					DirectoryPath = reader.GetString(2),
					Extension = reader.GetString(3),
					SizeBytes = (ulong)Math.Max(0L, reader.GetInt64(4)),
					LastModified = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)),
					IsArchive = reader.GetInt32(6) != 0,
					IsRoblox = reader.GetInt32(7) != 0,
					IsRepository = reader.GetInt32(8) != 0,
					IsDownloads = reader.GetInt32(9) != 0
				};

				result.Rank = Rank(result, parsed.Term, query.PinnedRoots ?? []);
				candidates.Add(result);
			}

			return candidates
				.OrderByDescending(x => x.Rank)
				.ThenByDescending(x => x.LastModified)
				.Take(query.MaxResults)
				.ToArray();
		}

		private async Task IndexDirectoryAsync(
			DirectoryInfo directory,
			int depth,
			bool insideRepository,
			FileIndexOptions options,
			HashSet<string> excludedRoots,
			HashSet<string> ignoredFolders,
			SqliteCommand insertFileCommand,
			SqliteParameter[] insertParameters,
			SqliteCommand insertContentCommand,
			SqliteParameter contentPathParameter,
			SqliteParameter contentExcerptParameter,
			string scanId,
			IProgress<FilesProScanProgress>? progress,
			IndexCounters counters,
			Action<IndexCounters> countsChanged,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (depth > options.MaxDepth || ignoredFolders.Contains(directory.Name) || IsExcluded(directory.FullName, excludedRoots))
				return;

			var childIsInsideRepository = insideRepository || Directory.Exists(Path.Combine(directory.FullName, ".git"));
			await InsertEntryAsync(directory, isDirectory: true, childIsInsideRepository, insertFileCommand, insertParameters, scanId, cancellationToken);

			counters = counters with
			{
				Files = counters.Files + 1,
				Directories = counters.Directories + 1
			};
			countsChanged(counters);

			if (counters.Directories % 25 == 0)
				progress?.Report(new("File index", directory.FullName, counters.Directories, counters.Files));

			try
			{
				foreach (var file in directory.EnumerateFiles())
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (counters.Files >= options.MaxFiles)
						return;

					await InsertEntryAsync(file, isDirectory: false, childIsInsideRepository, insertFileCommand, insertParameters, scanId, cancellationToken);

					if (options.IncludeTextContent && file.Length <= options.MaxContentBytes && LooksTextIndexable(file.Extension))
					{
						var excerpt = await ReadTextExcerptAsync(file, options.MaxContentBytes, cancellationToken);
						if (!string.IsNullOrWhiteSpace(excerpt))
						{
							contentPathParameter.Value = file.FullName;
							contentExcerptParameter.Value = excerpt;
							await insertContentCommand.ExecuteNonQueryAsync(cancellationToken);
						}
					}

					counters = counters with { Files = counters.Files + 1 };
					countsChanged(counters);

					if (counters.Files % 500 == 0)
					{
						progress?.Report(new("File index", file.FullName, counters.Directories, counters.Files));
						if (options.CpuThrottleDelayMs > 0)
							await Task.Delay(options.CpuThrottleDelayMs, cancellationToken);
						else
							await Task.Yield();
					}
				}

				foreach (var child in directory.EnumerateDirectories())
				{
					if (counters.Files >= options.MaxFiles)
						return;

					await IndexDirectoryAsync(
						child,
						depth + 1,
						childIsInsideRepository,
						options,
						excludedRoots,
						ignoredFolders,
						insertFileCommand,
						insertParameters,
						insertContentCommand,
						contentPathParameter,
						contentExcerptParameter,
						scanId,
						progress,
						counters,
						value =>
						{
							counters = value;
							countsChanged(value);
						},
						cancellationToken);
				}
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
			{
				logger.LogDebug(ex, "Files Pro file index skipped {Path}", directory.FullName);
			}
		}

		private static async Task InsertEntryAsync(
			FileSystemInfo item,
			bool isDirectory,
			bool insideRepository,
			SqliteCommand command,
			SqliteParameter[] parameters,
			string scanId,
			CancellationToken cancellationToken)
		{
			var extension = isDirectory ? string.Empty : Path.GetExtension(item.Name);
			var directoryPath = isDirectory
				? item.FullName
				: Path.GetDirectoryName(item.FullName) ?? string.Empty;

			parameters[0].Value = item.FullName;
			parameters[1].Value = item.Name;
			parameters[2].Value = directoryPath;
			parameters[3].Value = extension;
			parameters[4].Value = item is FileInfo file ? Math.Max(0L, file.Length) : 0L;
			parameters[5].Value = new DateTimeOffset(item.LastWriteTime).ToUnixTimeSeconds();
			parameters[6].Value = isDirectory ? 1 : 0;
			parameters[7].Value = ArchiveExtensions.Contains(extension) ? 1 : 0;
			parameters[8].Value = RobloxExtensions.Contains(extension) ? 1 : 0;
			parameters[9].Value = insideRepository ? 1 : 0;
			parameters[10].Value = IsDownloadsPath(item.FullName) ? 1 : 0;
			parameters[11].Value = scanId;

			await command.ExecuteNonQueryAsync(cancellationToken);
		}

		private static async Task<string> ReadTextExcerptAsync(FileInfo file, long maxBytes, CancellationToken cancellationToken)
		{
			try
			{
				var buffer = new byte[Math.Min(maxBytes, file.Length)];
				await using var stream = file.OpenRead();
				var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
				return Encoding.UTF8.GetString(buffer, 0, read);
			}
			catch
			{
				return string.Empty;
			}
		}

		private static ParsedFileSearch Parse(string text)
		{
			var terms = new List<string>();
			var builder = new ParsedFileSearchBuilder();
			foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var separatorIndex = token.IndexOf(':');
				if (separatorIndex <= 0)
				{
					terms.Add(token);
					continue;
				}

				var key = token[..separatorIndex].ToLowerInvariant();
				var value = token[(separatorIndex + 1)..];
				switch (key)
				{
					case "ext":
						builder.Extension = value.TrimStart('.');
						break;
					case "repo":
						builder.Repo = ParseBool(value);
						break;
					case "roblox":
						builder.Roblox = ParseBool(value);
						break;
					case "downloads":
						builder.Downloads = ParseBool(value);
						break;
					case "archive":
						builder.Archive = ParseBool(value);
						break;
					case "duplicate":
						builder.Duplicate = ParseBool(value);
						break;
					case "size":
						ParseSizeFilter(value, builder);
						break;
					case "modified":
						builder.ModifiedAfter = ParseModified(value);
						break;
					default:
						terms.Add(token);
						break;
				}
			}

			return builder.Build(string.Join(' ', terms));
		}

		private static bool? ParseBool(string value)
		{
			if (bool.TryParse(value, out var result))
				return result;

			return value is "1" or "yes" or "on";
		}

		private static void ParseSizeFilter(string value, ParsedFileSearchBuilder builder)
		{
			if (string.IsNullOrWhiteSpace(value))
				return;

			var comparison = value[0] is '>' or '<' ? value[0] : '=';
			var sizeText = comparison is '=' ? value : value[1..];
			var sizeBytes = ParseSizeBytes(sizeText);
			if (sizeBytes is null)
				return;

			if (comparison == '>')
				builder.MinSizeBytes = sizeBytes.Value + 1;
			else if (comparison == '<')
				builder.MaxSizeBytes = sizeBytes.Value - 1;
			else
			{
				builder.MinSizeBytes = sizeBytes.Value;
				builder.MaxSizeBytes = sizeBytes.Value;
			}
		}

		private static long? ParseSizeBytes(string value)
		{
			value = value.Trim().ToLowerInvariant();
			var multiplier = value switch
			{
				var text when text.EndsWith("tb") => 1024L * 1024L * 1024L * 1024L,
				var text when text.EndsWith("gb") => 1024L * 1024L * 1024L,
				var text when text.EndsWith("mb") => 1024L * 1024L,
				var text when text.EndsWith("kb") => 1024L,
				var text when text.EndsWith("b") => 1L,
				_ => 1L
			};

			var digits = value.TrimEnd('t', 'g', 'm', 'k', 'b');
			return double.TryParse(digits, out var parsed)
				? (long)(parsed * multiplier)
				: null;
		}

		private static DateTimeOffset? ParseModified(string value)
		{
			var now = DateTimeOffset.Now;
			return value.ToLowerInvariant() switch
			{
				"today" => now.Date,
				"yesterday" => now.Date.AddDays(-1),
				"thisweek" => now.AddDays(-7),
				"week" => now.AddDays(-7),
				"thismonth" => now.AddMonths(-1),
				"month" => now.AddMonths(-1),
				"thisyear" => now.AddYears(-1),
				"year" => now.AddYears(-1),
				_ => null
			};
		}

		private static int Rank(FileSearchResult result, string term, IReadOnlyList<string> pinnedRoots)
		{
			var rank = 0;
			if (!string.IsNullOrWhiteSpace(term))
			{
				if (string.Equals(Path.GetFileNameWithoutExtension(result.Name), term, StringComparison.OrdinalIgnoreCase))
					rank += 800;
				else if (result.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
					rank += 500;
				else if (result.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
					rank += 250;
				else if (result.Path.Contains(term, StringComparison.OrdinalIgnoreCase))
					rank += 120;
			}

			if (pinnedRoots.Any(root => result.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
				rank += 200;
			if (result.IsRepository)
				rank += 80;
			if (result.IsRoblox)
				rank += 60;
			if (result.IsDownloads)
				rank += 20;
			if (DateTimeOffset.Now - result.LastModified < TimeSpan.FromDays(7))
				rank += 100;
			else if (DateTimeOffset.Now - result.LastModified < TimeSpan.FromDays(30))
				rank += 40;

			return rank;
		}

		private static bool IsExcluded(string path, HashSet<string> excludedRoots)
			=> excludedRoots.Any(root => path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

		private static bool LooksTextIndexable(string extension)
			=> extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".py", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".lua", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".luau", StringComparison.OrdinalIgnoreCase);

		private static bool IsDownloadsPath(string path)
		{
			var downloads = Constants.UserEnvironmentPaths.DownloadsPath;
			return !string.IsNullOrWhiteSpace(downloads) &&
				path.StartsWith(downloads.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		}

		private static async Task EnsureDatabaseAsync(string databasePath, CancellationToken cancellationToken)
		{
			SQLitePCL.Batteries_V2.Init();
			Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

			await using var connection = CreateConnection(databasePath);
			await connection.OpenAsync(cancellationToken);
			await ExecuteAsync(connection, "PRAGMA journal_mode = WAL", cancellationToken);
			await ExecuteAsync(connection, "PRAGMA synchronous = NORMAL", cancellationToken);
			await ExecuteAsync(connection, "PRAGMA temp_store = FILE", cancellationToken);
			await ExecuteAsync(connection, "PRAGMA cache_size = -4096", cancellationToken);
			await ExecuteAsync(connection, "PRAGMA busy_timeout = 5000", cancellationToken);
			await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL)", cancellationToken);
			await ExecuteAsync(
				connection,
				"""
				CREATE TABLE IF NOT EXISTS files
				(
					path TEXT PRIMARY KEY,
					name TEXT NOT NULL,
					directory_path TEXT NOT NULL,
					extension TEXT NOT NULL,
					size_bytes INTEGER NOT NULL,
					last_modified_utc INTEGER NOT NULL,
					is_directory INTEGER NOT NULL,
					is_archive INTEGER NOT NULL,
					is_roblox INTEGER NOT NULL,
					is_repo INTEGER NOT NULL,
					is_downloads INTEGER NOT NULL,
					last_seen_scan_id TEXT NOT NULL
				)
				""",
				cancellationToken);
			await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS content (path TEXT PRIMARY KEY, excerpt TEXT NOT NULL)", cancellationToken);
			await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_files_name ON files(name)", cancellationToken);
			await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_files_extension ON files(extension)", cancellationToken);
			await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_files_size ON files(size_bytes)", cancellationToken);
			await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_files_modified ON files(last_modified_utc)", cancellationToken);
			await ExecuteAsync(connection, "INSERT OR IGNORE INTO metadata(key, value) VALUES ('schema_version', $value)", cancellationToken, ("$value", SchemaVersion.ToString()));
		}

		private static SqliteConnection CreateConnection(string databasePath)
		{
			var builder = new SqliteConnectionStringBuilder
			{
				DataSource = databasePath,
				Pooling = true
			};

			return new(builder.ToString());
		}

		private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
		{
			await using var command = connection.CreateCommand();
			command.CommandText = sql;
			foreach (var parameter in parameters)
				command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}

		private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
		{
			await using var command = connection.CreateCommand();
			command.CommandText = sql;
			return await command.ExecuteScalarAsync(cancellationToken);
		}

		private static string GetDatabasePath()
			=> Path.Combine(ApplicationData.Current.LocalFolder.Path, "FilesPro", "files-pro-index.db");

		private sealed record IndexCounters(int Files, int Directories);

		private sealed class ParsedFileSearchBuilder
		{
			public string? Extension { get; set; }

			public bool? Repo { get; set; }

			public bool? Roblox { get; set; }

			public bool? Downloads { get; set; }

			public bool? Archive { get; set; }

			public bool? Duplicate { get; set; }

			public long? MinSizeBytes { get; set; }

			public long? MaxSizeBytes { get; set; }

			public DateTimeOffset? ModifiedAfter { get; set; }

			public ParsedFileSearch Build(string term)
				=> new()
				{
					Term = term,
					Extension = Extension,
					Repo = Repo,
					Roblox = Roblox,
					Downloads = Downloads,
					Archive = Archive,
					Duplicate = Duplicate,
					MinSizeBytes = MinSizeBytes,
					MaxSizeBytes = MaxSizeBytes,
					ModifiedAfter = ModifiedAfter
				};
		}
	}
}
