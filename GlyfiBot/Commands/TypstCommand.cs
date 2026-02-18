using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using static GlyfiBot.Utils;

namespace GlyfiBot.Commands;

public partial class TypstCommand : ApplicationCommandModule<SlashCommandContext>
{
	private const string TYPST_VERSION = "v0.14.2";
	private const string SCRIPTS_REPO_NAME = "weekly-challenges-typst";

	private static readonly HttpClient _client = new();

	[SlashCommand("typst",
		"Does a Typst thing!")]
	[UsedImplicitly]
	public async Task ExecuteAsync()
	{
		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

		string typstExe = await SetupTypst();
		string scriptPath = await SetupScript();

		Process typstCmd = new() {StartInfo = new ProcessStartInfo(typstExe, ["compile", scriptPath]) {RedirectStandardOutput = true, RedirectStandardError = true}};
		typstCmd.Start();
		await typstCmd.WaitForExitAsync();

		await Context.ModifyEphemeralResponseAsync($"""
		                                            ```
		                                            {await typstCmd.StandardOutput.ReadToEndAsync()}
		                                            {await typstCmd.StandardError.ReadToEndAsync()}
		                                            ```
		                                            """);
	}

#region Setup Script

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
