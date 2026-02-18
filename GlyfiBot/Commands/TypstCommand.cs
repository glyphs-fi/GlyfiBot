using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using static GlyfiBot.Utils;

namespace GlyfiBot.Commands;

public enum ChallengeType
{
	Glyph,
	Ambigram,
}
public enum OutputFormat
{
	PDF,
	PNG,
	Both,
}
[SlashCommand("typst",
	"Generates an image/PDF using our Typst script",
	DefaultGuildPermissions = Permissions.Administrator)]
public partial class TypstCommand : ApplicationCommandModule<SlashCommandContext>
{
	private const string TYPST_VERSION = "v0.14.2";
	private const string SCRIPTS_REPO_NAME = "weekly-challenges-typst";
	private const string PPI_DESC = "Only used when the output_format is PNG or Both. If not provided, Typst defaults to 144";
	private const string END_DATE_NOT_IMPLEMENTED = "\n:warning: Provided `end_date` parameter was ignored, as the Typst script does not support that yet.";

	private static readonly HttpClient _client = new();

	/// To prevent multiple commands from running at the same time, each command has a blocker from this.
	/// This is necessary because file operations (like downloading the Typst Compiler and our Script) in the same folder at the same time could lead to issues.
	private static readonly ProgressTracker _progressTracker = new();
	public static void EndAfterError() => _progressTracker.End();

	[SubSlashCommand("announcement",
		"Generates an announcement")]
	[UsedImplicitly]
	public async Task Announcement(
		ChallengeType challengeType,
		string input,
		int weekNumber,
		string? startDate = null,
		string? endDate = null,
		OutputFormat outputFormat = OutputFormat.Both,
		[SlashCommandParameter(Description = PPI_DESC)]
		int? ppi = null
	)
	{
		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
		await _progressTracker.Start(Context);

		string typstExe = await SetupTypst();
		string scriptPath = await SetupScript();

		string toGenerate, inputKey;
		switch(challengeType)
		{
			case ChallengeType.Glyph:
				toGenerate = "glyph-announcement";
				inputKey = "announcement-glyph";
				break;
			case ChallengeType.Ambigram:
				toGenerate = "ambigram-announcement";
				inputKey = "announcement-ambi";
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(challengeType), challengeType, null);
		}

		string outputDir = Path.Join(Program.ANNOUNCEMENTS_DIR, challengeType.ToString());

		List<string> args =
		[
			"--input", $"to-generate={toGenerate}",
			"--input", $"{inputKey}={input}",
			"--input", $"current-week={weekNumber}",
		];
		if (startDate != null) args.AddRange(["--input", $"current-date={startDate}"]);

		List<AttachmentProperties> attachments = [];
		string content = "Done!" + await GenerateAttachments(typstExe, scriptPath, outputDir, outputFormat, args, ppi, attachments);

		if (endDate != null) content += END_DATE_NOT_IMPLEMENTED;

		await ModifyResponseAsync(msg =>
		{
			msg.Flags = MessageFlags.Ephemeral | MessageFlags.SuppressEmbeds;
			msg.Content = content;
			msg.Attachments = attachments;
		});

		_progressTracker.End();
	}


	[SubSlashCommand("showcase",
		"Generates a showcase")]
	[UsedImplicitly]
	public async Task Showcase(
		ChallengeType challengeType,
		string input,
		int weekNumber,
		string start,
		[SlashCommandParameter(Description = "If not provided, will select till the end")]
		string? end = null,
		string? startDate = null,
		string? endDate = null,
		OutputFormat outputFormat = OutputFormat.Both,
		[SlashCommandParameter(Description = PPI_DESC)]
		int? ppi = null
	)
	{
		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
		await _progressTracker.Start(Context);

		string typstExe = await SetupTypst();
		string scriptPath = await SetupScript();

		string toGenerate, inputKey;
		switch(challengeType)
		{
			case ChallengeType.Glyph:
				toGenerate = "glyph-showcase";
				inputKey = "showcase-glyph";
				break;
			case ChallengeType.Ambigram:
				toGenerate = "ambigram-showcase";
				inputKey = "showcase-ambi";
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(challengeType), challengeType, null);
		}

		string outputDir = Path.Join(Program.SHOWCASES_DIR, challengeType.ToString(), $"{Context.Interaction.Id}");
		string imagesDir = Path.Join(outputDir, "images");
		Directory.CreateDirectory(imagesDir);

		//TODO: Download all the images

		string scriptDir = Path.GetDirectoryName(scriptPath) ?? throw new InvalidOperationException($"Could not find script directory of path `{scriptPath}`");
		List<string> args =
		[
			"--input", $"to-generate={toGenerate}",
			"--input", $"{inputKey}={input}",
			"--input", $"current-week={weekNumber}",
			"--input", $"image-dir={Path.GetRelativePath(scriptDir, imagesDir)}",
		];
		if (startDate != null) args.AddRange(["--input", $"showcase-date={startDate}"]);

		List<AttachmentProperties> attachments = [];
		string content = "Done!" + await GenerateAttachments(typstExe, scriptPath, outputDir, outputFormat, args, ppi, attachments);

		if (endDate != null) content += END_DATE_NOT_IMPLEMENTED;

		await ModifyResponseAsync(msg =>
		{
			msg.Flags = MessageFlags.Ephemeral | MessageFlags.SuppressEmbeds;
			msg.Content = content;
			msg.Attachments = attachments;
		});

		_progressTracker.End();
	}

#region Run Script with Typst

	private async Task<string> GenerateAttachments(string typstExe, string scriptPath, string outputDir, OutputFormat outputFormat, List<string> args, int? ppi, List<AttachmentProperties> attachments)
	{
		Directory.CreateDirectory(outputDir);
		string content = "";

		if (outputFormat is OutputFormat.PDF or OutputFormat.Both)
		{
			content += await GenerateAttachmentForFormat(typstExe, scriptPath, outputDir, OutputFormat.PDF, args, attachments);
		}

		if (outputFormat is OutputFormat.PNG or OutputFormat.Both)
		{
			content += await GenerateAttachmentForFormat(typstExe, scriptPath, outputDir, OutputFormat.PNG, ppi == null ? args : [..args, "--ppi", $"{ppi}"], attachments);
		}

		return content;
	}

	private async Task<string> GenerateAttachmentForFormat(string typstExe, string scriptPath, string outputDir, OutputFormat outputFormat, IEnumerable<string> args, List<AttachmentProperties> attachments)
	{
		if (outputFormat == OutputFormat.Both) throw new ArgumentOutOfRangeException(nameof(outputFormat), outputFormat, null);

		string fileType = outputFormat.ToString().ToUpper();
		string fileName = $"{Context.Interaction.Id}.{fileType.ToLower()}";
		string outputFile = Path.Join(outputDir, fileName);

		string rootDir = Directory.GetCurrentDirectory();

		ProcessStartInfo startInfo = new(typstExe, ["compile", scriptPath, "--root", rootDir, ..args, outputFile]) {RedirectStandardOutput = true, RedirectStandardError = true};
		Process typstCmd = new() {StartInfo = startInfo};
		typstCmd.Start();
		await typstCmd.WaitForExitAsync();

		int exitCode = typstCmd.ExitCode;
		string stdout = await typstCmd.StandardOutput.ReadToEndAsync();
		string stderr = await typstCmd.StandardError.ReadToEndAsync();

		string content = "";
		if (exitCode != 0) content += $" ({fileType} exited with: {exitCode})";
		if (!stdout.IsWhiteSpace()) attachments.Add(new AttachmentProperties($"{fileType}_StdOut.txt", new MemoryStream(Encoding.UTF8.GetBytes(stdout))));
		if (!stderr.IsWhiteSpace()) attachments.Add(new AttachmentProperties($"{fileType}_StdErr.txt", new MemoryStream(Encoding.UTF8.GetBytes(stderr))));
		if (File.Exists(outputFile)) attachments.Add(new AttachmentProperties(fileName, new FileStream(outputFile, FileMode.Open)));

		return content;
	}

#endregion

#region Setup Script

	/// <summary>
	/// Installs the latest version of our Typst Script in the bot's data directory.
	/// </summary>
	/// <returns>The path to our Typst Script's main.typ (the file itself, not the containing directory)</returns>
	/// <exception cref="FileNotFoundException">If the download did not contain the main.typ file</exception>
	private async Task<string> SetupScript()
	{
		string latestCommitHash = await GetLatestCommitHash();

		string scriptDir = Path.Join(Program.TYPST_SCRIPT_DIR, $"{SCRIPTS_REPO_NAME}-{latestCommitHash}");
		if (!Directory.Exists(scriptDir))
		{
			await Context.ModifyEphemeralResponseAsync("Downloading script... (This will only happen once)");

			Directory.CreateDirectory(scriptDir);
			string zipPath = Path.Join(Program.TYPST_SCRIPT_DIR, $"{latestCommitHash}.zip");
			{
				await using Stream networkStream = await _client.GetStreamAsync($"https://github.com/glyphs-fi/{SCRIPTS_REPO_NAME}/archive/{latestCommitHash}.zip");
				await using FileStream fileStream = new(zipPath, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
			}

			await Context.ModifyEphemeralResponseAsync("Extracting script zip... (This will only happen once)");
			await ExtractArchive(zipPath);
		}

		string scriptPath = Path.Join(scriptDir, "main.typ");
		return File.Exists(scriptPath) ? scriptPath : throw new FileNotFoundException("Could not find `main.typ` in the script folder!");
	}

	[GeneratedRegex("""<meta\s+property=(["'])og:url\1\s+content=(["']).+/commit/(.+?)/?\2\s*/>""")]
	private static partial Regex HashRegex();

	private static async Task<string> GetLatestCommitHash()
	{
		await using Stream networkStream = await _client.GetStreamAsync($"https://github.com/glyphs-fi/{SCRIPTS_REPO_NAME}/commit/main");
		using StreamReader streamReader = new(networkStream);

		Regex pattern = HashRegex();
		while(await streamReader.ReadLineAsync() is {} line)
		{
			Match match = pattern.Match(line);
			if (match.Success)
			{
				return match.Groups[3].Value;
			}
		}

		throw new Exception("Failed to retrieve the latest commit hash of the Typst script!");
	}

#endregion

#region Setup Typst

	/// <summary>
	/// Installs the Typst Compiler in the bot's data directory.
	/// </summary>
	/// <returns>The path to the Typst Compiler executable (the file itself, not the containing directory)</returns>
	/// <exception cref="FileNotFoundException">If the download did not contain a Typst executable</exception>
	private async Task<string> SetupTypst()
	{
		string typstDownloadURL = GetTypstDownloadURLForPlatform();

		string typstExeVersionDir = Path.Join(Program.TYPST_EXE_DIR, TYPST_VERSION);
		if (!Directory.Exists(typstExeVersionDir))
		{
			await Context.ModifyEphemeralResponseAsync("Downloading Typst... (This will only happen once)");

			Directory.CreateDirectory(typstExeVersionDir);
			string archivePath = Path.Join(typstExeVersionDir, Path.GetFileName(typstDownloadURL));
			{
				await using Stream networkStream = await _client.GetStreamAsync(typstDownloadURL);
				await using FileStream fileStream = new(archivePath, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
			}

			await Context.ModifyEphemeralResponseAsync("Extracting Typst... (This will only happen once)");
			await ExtractArchive(archivePath);
		}

		string? exeLocation = FindExe(typstExeVersionDir, "typst");
		return exeLocation ?? throw new FileNotFoundException("Could not find a Typst executable in the unpacked archive!");
	}

	private const string URL = $"https://github.com/typst/typst/releases/download/{TYPST_VERSION}";
	private const string URL_LINUX_X64 = $"{URL}/typst-x86_64-unknown-linux-musl.tar.xz";
	private const string URL_LINUX_ARM64 = $"{URL}/typst-aarch64-unknown-linux-musl.tar.xz";
	private const string URL_WIN_X64 = $"{URL}/typst-x86_64-pc-windows-msvc.zip";

	[SuppressMessage("ReSharper", "SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault")]
	private static string GetTypstDownloadURLForPlatform()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			return RuntimeInformation.OSArchitecture switch
			{
				Architecture.X64 => URL_LINUX_X64,
				Architecture.Arm64 => URL_LINUX_ARM64,
				_ => throw new PlatformNotSupportedException("The bot is running on a server that is not of an Architecture for Linux that this bot supports, so Typst cannot be installed!"),
			};
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return RuntimeInformation.OSArchitecture switch
			{
				Architecture.X64 => URL_WIN_X64,
				_ => throw new PlatformNotSupportedException("The bot is running on a server that is not of an Architecture for Windows that this bot supports, so Typst cannot be installed"),
			};
		}

		throw new PlatformNotSupportedException("The bot is running on a server that is not of an Operating System that this bot supports, so Typst cannot be installed!");
	}

#endregion

}
