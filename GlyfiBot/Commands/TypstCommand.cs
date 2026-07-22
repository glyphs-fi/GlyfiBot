using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using static GlyfiBot.Utils;

namespace GlyfiBot.Commands;

public enum ChallengeType
{
	Glyph,
	Ambigram,
}
public static class ChallengeTypeExtensions
{
	extension(ChallengeType challengeType)
	{
		/// <returns>lowercase</returns>
		private string GetLongName() => challengeType switch
		{
			ChallengeType.Glyph => "glyph",
			ChallengeType.Ambigram => "ambigram",
			_ => throw new ArgumentOutOfRangeException(nameof(challengeType), challengeType, null),
		};

		/// <returns>lowercase</returns>
		private string GetShortName() => challengeType switch
		{
			ChallengeType.Glyph => "glyph",
			ChallengeType.Ambigram => "ambi",
			_ => throw new ArgumentOutOfRangeException(nameof(challengeType), challengeType, null),
		};

		public string GetNameForDir() => challengeType.GetLongName().UpperFirst();

		public string GetNameForSubmission() => challengeType.GetShortName().UpperFirst();

		private (string, string) ForChallenge(string challengeName) => ($"{challengeType.GetLongName()}-{challengeName}", $"{challengeName}-{challengeType.GetShortName()}");

		public (string toGenerate, string inputKey) ForAnnouncement() => challengeType.ForChallenge("announcement");

		public (string toGenerate, string inputKey, string numColsArgName) ForShowcase()
		{
			(string toGenerate, string inputKey) = challengeType.ForChallenge("showcase");
			string numColsArgName = $"{challengeType.GetShortName()}-showcase-num-cols";
			return (toGenerate, inputKey, numColsArgName);
		}

		public (string toGenerate, string userNameKey, string nickNameKey) ForWinners(string level) =>
			($"{challengeType.GetLongName()}-{level}", $"{challengeType.GetShortName()}-winner-{level}-username", $"{challengeType.GetShortName()}-winner-{level}-nickname");
	}
}
public enum OutputFormat
{
	PDF,
	PNG,
	Both,
}
[SuppressMessage("ReSharper", "SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault")]
public static class OutputFormatExtensions
{
	extension(OutputFormat outputFormat)
	{
		public string GetLower() => outputFormat switch
		{
			OutputFormat.PDF => "pdf",
			OutputFormat.PNG => "png",
			_ => throw new ArgumentOutOfRangeException(nameof(outputFormat), outputFormat, null),
		};

		public string GetUpper() => outputFormat switch
		{
			OutputFormat.PDF => "PDF",
			OutputFormat.PNG => "PNG",
			_ => throw new ArgumentOutOfRangeException(nameof(outputFormat), outputFormat, null),
		};
	}
}
[SlashCommand(COMMAND_NAME,
	"Generates an image/PDF using our Typst script",
	DefaultGuildPermissions = Permissions.Administrator)]
public class TypstCommand : ApplicationCommandModule<SlashCommandContext>
{
	public const string COMMAND_NAME = "typst";
	private const string TYPST_VERSION = "v0.14.2";
	private const string SCRIPTS_REPO_NAME = "weekly-challenges-typst";
	private const string PPI_DESC = "Only used when the output_format is PNG or Both. If not provided, Typst defaults to 144";

	/// All the file types Typst supports
	/// https://typst.app/docs/reference/visualize/image/#parameters-format
	private static readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase) {".png", ".jpg", ".jpeg", ".gif", ".svg", ".pdf", ".webp"};

	/// To prevent multiple commands from running at the same time, each command has a blocker from this.
	/// This is necessary because file operations (like downloading the Typst Compiler and our Script) in the same folder at the same time could lead to issues.
	private static readonly ProgressTracker _progressTracker = new();
	public static void EndAfterError() => _progressTracker.End();

	[SubSlashCommand("announcement",
		"Generates an announcement image")]
	[UsedImplicitly]
	public async Task Announcement(
		ChallengeType challengeType,
		//
		string input,
		//
		[SlashCommandParameter(Description = "The number of the current week")]
		int currentWeek,
		//
		string? startDate = null,
		string? endDate = null,
		//
		OutputFormat outputFormat = OutputFormat.PNG,
		//
		[SlashCommandParameter(Description = PPI_DESC)]
		int? ppi = null
	)
	{
		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
		await _progressTracker.Start(Context);

		string typstExe = await SetupTypst(Context);
		string scriptPath = await SetupScript(Context);

		(string toGenerate, string inputKey) = challengeType.ForAnnouncement();

		string outputDir = Path.Join(Program.ANNOUNCEMENTS_DIR, challengeType.GetNameForDir(), $"{Context.Interaction.Id}");

		List<string> args =
		[
			"--input", $"to-generate={toGenerate}",
			"--input", $"{inputKey}={input}",
			"--input", $"current-week={currentWeek}",
		];
		if (startDate is not null) args.AddRange(["--input", $"announcement-start-date={startDate}"]);
		if (endDate is not null) args.AddRange(["--input", $"announcement-end-date={endDate}"]);

		List<AttachmentProperties> attachments = [];
		string outputFilename = $"w{currentWeek}_{challengeType.GetNameForDir()}_{nameof(Announcement)}";
		string content = "Done!" + await GenerateAttachments(typstExe, scriptPath, outputDir, outputFilename, outputFormat, args, ppi, attachments);

		await ModifyResponseAsync(msg =>
		{
			msg.Flags = MessageFlags.Ephemeral;
			msg.Content = content;
			msg.Attachments = attachments;
		});

		_progressTracker.End();
	}


	[SubSlashCommand("showcase",
		"Generates a showcase image")]
	[UsedImplicitly]
	public async Task Showcase(
		ChallengeType challengeType,
		//
		string input,
		//
		[SlashCommandParameter(Description = "The number of the current week")]
		int currentWeek,
		//
		[SlashCommandParameter(Description = "Message ID")]
		string start,
		//
		[SlashCommandParameter(Description = "Message ID. If not provided, will select until the end")]
		string? end = null,
		//
		int? columns = null,
		//
		string? startDate = null,
		string? endDate = null,
		//
		OutputFormat outputFormat = OutputFormat.PNG,
		//
		[SlashCommandParameter(Description = PPI_DESC)]
		int? ppi = null
	)
	{
		// Check the emoji first, to let the user know if they forgot to set it, before they can get any other errors from the next steps
		ReactionEmojiProperties? emoji = SetTheEmojiCommand.GetSubmissionEmoji(Context.Channel);
		if (emoji is null)
		{
			throw new SimpleCommandFailException("""
			                                     Emoji has not been set for this channel! Use `/set-emoji submission` to set the emoji first
			                                     -# You may also want to set a disqualification emoji with `/set-emoji disqualification`
			                                     """);
		}

		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
		await _progressTracker.Start(Context);

		string typstExe = await SetupTypst(Context);
		string scriptPath = await SetupScript(Context);

		(string toGenerate, string inputKey, string numColsArgName) = challengeType.ForShowcase();

		string outputDir = Path.Join(Program.SHOWCASES_DIR, challengeType.GetNameForDir(), $"{Context.Interaction.Id}");
		string imagesDir = Path.Join(outputDir, "images");
		Directory.CreateDirectory(imagesDir);

		// Select messages in the provided range
		if (!ulong.TryParse(start, null, out ulong idStart))
		{
			throw new SimpleCommandFailException("`start` needs to be a number: the Message ID");
		}

		await VerifyThatMessageIsInChannel(Context, idStart);

		ulong? idEnd = null;
		if (end is not null)
		{
			if (!ulong.TryParse(end, null, out ulong idEndLocal))
			{
				throw new SimpleCommandFailException("`end` needs to be a number: the Message ID");
			}

			// If the order is wrong, swap them into the correct order
			if (idStart > idEndLocal) (idStart, idEndLocal) = (idEndLocal, idStart);

			await VerifyThatMessageIsInChannel(Context, idEndLocal);

			idEnd = idEndLocal;
		}

		List<RestMessage> messages = await GetMessagesBetweenAsync(Context, idStart, idEnd);

		// Filter submissions from the messages
		(Dictionary<User, List<Attachment>> submissions, uint _) = await SelectRangeCommand.FilterSubmissionsFromMessagesAsync(Context.Guild, Context.Channel, messages, emoji);
		List<Attachment> allSubmissions = submissions.Values //
			.SelectMany(attachments => attachments) //
			.Where(attachment => _supportedExtensions.Contains(Path.GetExtension(attachment.FileName))) //
			.ToList();

		// Check if we have any submissions at all
		if (allSubmissions.Count < 1)
		{
			throw new SimpleCommandFailException("There are no submissions in the selected range!");
		}

		// Get maximum number of supported submissions from the Typst Script
		string scriptDir = GetScriptDir(scriptPath);
		{
			List<string> labels = await GetLabels(typstExe, scriptDir);
			if (allSubmissions.Count > labels.Count)
			{
				throw new SimpleCommandFailException($"There are more submissions ({allSubmissions.Count}) than the Typst Script can display ({labels.Count})!");
			}
		}

		// Download submission message attachments
		for(int i = 0; i < allSubmissions.Count; i++)
		{
			Attachment submission = allSubmissions[i];
			string path = Path.Join(imagesDir, $"{challengeType.GetNameForSubmission()}_{i + 1}");

			await using Stream networkStream = await Program.HttpClient.GetStreamAsync(submission.Url);
			await using FileStream fileStream = new(path, FileMode.CreateNew);
			await networkStream.CopyToAsync(fileStream);
		}

		List<string> args =
		[
			"--input", $"to-generate={toGenerate}",
			"--input", $"{inputKey}={input}",
			"--input", $"current-week={currentWeek}",
			"--input", $"image-dir={Path.GetRelativePath(scriptDir, imagesDir)}",
		];
		if (columns is not null)
		{
			if (columns < 1) throw new SimpleCommandFailException($"Argument `columns` was {columns}, but it should be _1 or more_!");
			if (columns > allSubmissions.Count) throw new SimpleCommandFailException($"Argument `columns` was {columns}, but it should be _less than or equal to_ the amount of submissions ({allSubmissions.Count})!");
			args.AddRange(["--input", $"{numColsArgName}={columns}"]);
		}
		if (startDate is not null) args.AddRange(["--input", $"showcase-start-date={startDate}"]);
		if (endDate is not null) args.AddRange(["--input", $"showcase-end-date={endDate}"]);

		List<AttachmentProperties> attachments = [];
		string outputFilename = $"w{currentWeek}_{challengeType.GetNameForDir()}_{nameof(Showcase)}";
		string content = "Done!" + await GenerateAttachments(typstExe, scriptPath, outputDir, outputFilename, outputFormat, args, ppi, attachments);

		await ModifyResponseAsync(msg =>
		{
			msg.Flags = MessageFlags.Ephemeral;
			msg.Content = content;
			msg.Attachments = attachments;
		});

		_progressTracker.End();
	}

	[SubSlashCommand("winners",
		"Generates a winners image")]
	[UsedImplicitly]
	public async Task Winners(
		ChallengeType challengeType,
		//
		[SlashCommandParameter(Description = "The number of the current week")]
		int currentWeek,
		//
		[SlashCommandParameter(Description = "Message ID")]
		string firstPlace,
		[SlashCommandParameter(Description = "Optional display name override for first place")]
		string? firstPlaceNameOverride = null,
		//
		[SlashCommandParameter(Description = "Message ID")]
		string? secondPlace = null,
		[SlashCommandParameter(Description = "Optional display name override for second place")]
		string? secondPlaceNameOverride = null,
		//
		[SlashCommandParameter(Description = "Message ID")]
		string? thirdPlace = null,
		[SlashCommandParameter(Description = "Optional display name override for third place")]
		string? thirdPlaceNameOverride = null,
		//
		OutputFormat outputFormat = OutputFormat.PNG,
		//
		[SlashCommandParameter(Description = PPI_DESC)]
		int? ppi = null
	)
	{
		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
		await _progressTracker.Start(Context);

		string typstExe = await SetupTypst(Context);
		string scriptPath = await SetupScript(Context);

		string outputDir = Path.Join(Program.WINNERS_DIR, challengeType.ToString(), $"{Context.Interaction.Id}");
		string imagesDir = Path.Join(outputDir, "images");
		Directory.CreateDirectory(imagesDir);
		string pfpDir = Path.Join(imagesDir, "pfp");
		Directory.CreateDirectory(pfpDir);

		string scriptDir = Path.GetDirectoryName(scriptPath) ?? throw new InvalidOperationException($"Could not find script directory of path `{scriptPath}`");
		List<string> args =
		[
			"--input", $"current-week={currentWeek}",
			"--input", $"image-dir={Path.GetRelativePath(scriptDir, imagesDir)}",
		];

		string content = "Done!";
		List<AttachmentProperties> attachments = [];
		await FillAttachmentsForWinnerLevel("first", firstPlace, firstPlaceNameOverride);
		if (secondPlace is not null)
			await FillAttachmentsForWinnerLevel("second", secondPlace, secondPlaceNameOverride);
		if (thirdPlace is not null)
			await FillAttachmentsForWinnerLevel("third", thirdPlace, thirdPlaceNameOverride);

		await ModifyResponseAsync(msg =>
		{
			msg.Flags = MessageFlags.Ephemeral;
			msg.Content = content;
			msg.Attachments = attachments;
		});

		_progressTracker.End();

		return;

		// Returns true if success, false if fail
		async Task FillAttachmentsForWinnerLevel(string level, string stringId, string? displayNameOverride)
		{
			// Validate stringId
			if (!ulong.TryParse(stringId, null, out ulong messageId))
			{
				throw new SimpleCommandFailException($"`{level}_place` needs to be a number: the Message ID");
			}

			// Retrieve Message from ID
			RestMessage message = await VerifyThatMessageIsInChannel(Context, messageId);

			// Download author's profile picture
			ProfilePicturesCommand.DownloadFile avatarDownloadFile = await ProfilePicturesCommand.GetAvatar(message.Author, DownloadFormat.PNG, false, AnimatedDownloadFormat.Original, FilenameType.UserName);
			{
				string path = Path.Join(pfpDir, avatarDownloadFile.Filename);
				if (!File.Exists(path))
				{
					await using Stream networkStream = await Program.HttpClient.GetStreamAsync(avatarDownloadFile.DownloadUrl);
					await using FileStream fileStream = new(path, FileMode.CreateNew);
					await networkStream.CopyToAsync(fileStream);
				}
			}

			// Download the submission
			{
				Attachment submissionAttachment = message.Attachments.First(attachment => _supportedExtensions.Contains(Path.GetExtension(attachment.FileName)));
				string path = Path.Join(imagesDir, $"{challengeType.GetNameForSubmission()}Winner{level.UpperFirst()}");

				await using Stream networkStream = await Program.HttpClient.GetStreamAsync(submissionAttachment.Url);
				await using FileStream fileStream = new(path, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
			}

			(string toGenerate, string userNameKey, string nickNameKey) = challengeType.ForWinners(level);
			string profilePictureFileName = Path.GetFileNameWithoutExtension(avatarDownloadFile.Filename);
			string displayName = displayNameOverride ?? await message.Author.GetNickNameAsync(Context.Guild);
			List<string> localArgs =
			[
				..args,
				"--input", $"to-generate={toGenerate}",
				"--input", $"{userNameKey}={profilePictureFileName}",
				"--input", $"{nickNameKey}={displayName}",
			];
			string outputFilename = $"w{currentWeek}_{challengeType.GetNameForDir()}_{nameof(Winners)}_{level.UpperFirst()}";
			content += await GenerateAttachments(typstExe, scriptPath, outputDir, outputFilename, outputFormat, localArgs, ppi, attachments);
		}
	}

#region Run Script with Typst

	private static async Task<string> GenerateAttachments(
		string typstExe,
		string scriptPath,
		string outputDir,
		string outputFilename,
		OutputFormat outputFormat,
		List<string> args,
		int? ppi,
		List<AttachmentProperties> attachments
	)
	{
		const string header = "--------------------------------------- {0} Export ---------------------------------------\n";
		Directory.CreateDirectory(outputDir);
		string content = "";

		StringBuilder combinedStdout = new();
		StringBuilder combinedStderr = new();
		if (outputFormat is OutputFormat.PDF or OutputFormat.Both)
		{
			content += await RunForOutputFormat(OutputFormat.PDF, args);
		}

		if (outputFormat is OutputFormat.PNG or OutputFormat.Both)
		{
			content += await RunForOutputFormat(OutputFormat.PNG, ppi is null ? args : [..args, "--ppi", $"{ppi}"]);
		}

		if (combinedStdout.Length > 0) attachments.Add(new AttachmentProperties($"{outputFilename}_StdOut.txt", new MemoryStream(Encoding.UTF8.GetBytes(combinedStdout.ToString()))));
		if (combinedStderr.Length > 0) attachments.Add(new AttachmentProperties($"{outputFilename}_StdErr.txt", new MemoryStream(Encoding.UTF8.GetBytes(combinedStderr.ToString()))));

		return content;

		async Task<string> RunForOutputFormat(OutputFormat thisOutputFormat, IEnumerable<string> thisArgs)
		{
			(int exitCode, string stdout, string stderr) = await GenerateAttachmentForFormat(typstExe, scriptPath, outputDir, outputFilename, thisOutputFormat, thisArgs, attachments);
			string filename = $"{Path.GetFileNameWithoutExtension(outputFilename)}.{thisOutputFormat.GetLower()}";
			string headerLocal = string.Format(header, filename);
			if (!stdout.IsWhiteSpace()) combinedStdout.AppendLine(headerLocal + stdout);
			if (!stderr.IsWhiteSpace()) combinedStderr.AppendLine(headerLocal + stderr);
			return exitCode != 0 ? $" ({filename} exited with: {exitCode})" : "";
		}
	}

	private static async Task<(int exitCode, string stdout, string stderr)> GenerateAttachmentForFormat(
		string typstExe,
		string scriptPath,
		string outputDir,
		string outputFilename,
		OutputFormat outputFormat,
		IEnumerable<string> args,
		List<AttachmentProperties> attachments
	)
	{
		if (outputFormat == OutputFormat.Both) throw new ArgumentOutOfRangeException(nameof(outputFormat), outputFormat, null);

		string fileName = $"{outputFilename}.{outputFormat.GetLower()}";
		string outputFile = Path.Join(outputDir, fileName);

		string rootDir = Directory.GetCurrentDirectory();
		string scriptDir = GetScriptDir(scriptPath);
		string fontsDir = Path.Join(scriptDir, "fonts");

		ProcessStartInfo startInfo = new(typstExe, [
			"compile", scriptPath,
			"--root", rootDir,
			"--ignore-system-fonts",
			"--font-path", fontsDir,
			..args,
			outputFile,
		]) {RedirectStandardOutput = true, RedirectStandardError = true};
		Process typstCmd = new() {StartInfo = startInfo};
		typstCmd.Start();
		await typstCmd.WaitForExitAsync();

		if (File.Exists(outputFile)) attachments.Add(new AttachmentProperties(fileName, new FileStream(outputFile, FileMode.Open)));

		return (
			typstCmd.ExitCode,
			await typstCmd.StandardOutput.ReadToEndAsync(),
			await typstCmd.StandardError.ReadToEndAsync()
			);
	}

	public static async Task<List<string>> GetLabels(string typstExe, string scriptDir)
	{
		string globalConfigScript = Path.Join(scriptDir, "global-config.typ");
		string labelsJson = await QueryTypstScript(typstExe, globalConfigScript, ["--one", "--field", "value", "<LABEL-SEQUENCE>"]);
		return JsonSerializer.Deserialize(labelsJson, ToJson.Default.ListString)!;
	}

	private static async Task<string> QueryTypstScript(string typstExe, string typstFile, IEnumerable<string> args)
	{
		string rootDir = Directory.GetCurrentDirectory();
		string scriptDir = GetScriptDir(typstFile);
		string fontsDir = Path.Join(scriptDir, "fonts");

		ProcessStartInfo startInfo = new(typstExe, [
			"query", typstFile,
			"--root", rootDir,
			"--ignore-system-fonts",
			"--font-path", fontsDir,
			..args,
		])
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
		};
		Process typstCmd = new() {StartInfo = startInfo};
		typstCmd.Start();
		await typstCmd.WaitForExitAsync();

		return await typstCmd.StandardOutput.ReadToEndAsync();
	}

	/// Gets the root script dir from the scriptPath, because scriptPath may not necessarily be directly in the root
	public static string GetScriptDir(string scriptPath)
	{
		string scriptDir = scriptPath;
		while(!scriptDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last().StartsWith(SCRIPTS_REPO_NAME))
		{
			scriptDir = Path.GetDirectoryName(scriptDir) ?? throw new InvalidOperationException($"Could not find script directory of path `{scriptPath}`; currently at `{scriptDir}`");
			// ↑ This null-check+throw protects against arriving at the root or if the while loop would go on forever
		}
		return scriptDir;
	}

#endregion

#region Setup Script

	/// <summary>
	/// Installs the latest version of our Typst Script in the bot's data directory.
	/// </summary>
	/// <returns>The path to our Typst Script's main.typ (the file itself, not the containing directory)</returns>
	/// <exception cref="FileNotFoundException">If the download did not contain the main.typ file</exception>
	public static async Task<string> SetupScript(SlashCommandContext context)
	{
		(string scriptDir, bool _) = await DownloadRepo(SCRIPTS_REPO_NAME, Program.TYPST_SCRIPT_DIR,
			onDownloading: async () => await context.ModifyEphemeralResponseAsync("Downloading script... (This will only happen once)"),
			onExtracting: async () => await context.ModifyEphemeralResponseAsync("Extracting script zip... (This will only happen once)")
		);
		string scriptPath = Path.Join(scriptDir, "main.typ");
		return File.Exists(scriptPath) ? scriptPath : throw new FileNotFoundException("Could not find `main.typ` in the script folder!");
	}

#endregion

#region Setup Typst

	/// <summary>
	/// Installs the Typst Compiler in the bot's data directory.
	/// </summary>
	/// <returns>The path to the Typst Compiler executable (the file itself, not the containing directory)</returns>
	/// <exception cref="FileNotFoundException">If the download did not contain a Typst executable</exception>
	public static async Task<string> SetupTypst(SlashCommandContext context)
	{
		string filename = SwitchOnPlatformArch(
			linuxX64: "typst-x86_64-unknown-linux-musl.tar.xz",
			linuxArm64: "typst-aarch64-unknown-linux-musl.tar.xz",
			winX64: "typst-x86_64-pc-windows-msvc.zip"
		);
		(string typstDownloadURL, string remoteHash) = await GetReleaseAsset("typst", "typst", TYPST_VERSION, filename);

		string typstExeVersionDir = Path.Join(Program.TYPST_EXE_DIR, TYPST_VERSION);
		if (!Directory.Exists(typstExeVersionDir) || DirectoryEmpty(typstExeVersionDir))
		{
			await context.ModifyEphemeralResponseAsync("Downloading Typst... (This will only happen once)");

			Directory.CreateDirectory(typstExeVersionDir);
			string archivePath = Path.Join(typstExeVersionDir, filename);
			{
				await using Stream networkStream = await Program.HttpClient.GetStreamAsync(typstDownloadURL);
				await using FileStream fileStream = new(archivePath, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
			}

			string localHash = await HashFile(archivePath);
			if (!string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
			{
				File.Delete(archivePath);
				Directory.Delete(typstExeVersionDir);
				throw new PlatformNotSupportedException($"Failed to verify the Typst download!\nLocal hash `{localHash.ToLower()}` did not match remote hash `{remoteHash.ToLower()}`");
			}

			await context.ModifyEphemeralResponseAsync("Extracting Typst... (This will only happen once)");
			await ExtractArchive(archivePath);
		}

		string? exeLocation = FindExe(typstExeVersionDir, "typst");
		return exeLocation ?? throw new FileNotFoundException("Could not find a Typst executable in the unpacked archive!");
	}

#endregion

}
