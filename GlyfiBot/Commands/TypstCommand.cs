using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using static GlyfiBot.Utils;

namespace GlyfiBot.Commands;

public partial class TypstCommand : ApplicationCommandModule<SlashCommandContext>
{
	private const string TYPST_VERSION = "v0.14.2";
	private const string SCRIPTS_REPO_NAME = "weekly-challenges-typst";

	[SlashCommand("typst",
		"Does a Typst thing!")]
	[UsedImplicitly]
	public async Task ExecuteAsync()
	{
		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
		using HttpClient client = new();

		string? typstExe = await SetupTypst(client);
		if (typstExe is null)
		{
			await Context.ModifyEphemeralResponseAsync("Typst failed to install!");
			return;
		}

		await Context.ModifyEphemeralResponseAsync($"Typst found at `{typstExe}`");

		Process typstCmd = new() {StartInfo = new ProcessStartInfo(typstExe, ["--version"]) {RedirectStandardOutput = true}};
		typstCmd.Start();
		await typstCmd.WaitForExitAsync();

		await Context.ModifyEphemeralResponseAsync($"Typst found at `{typstExe}` with version `{await typstCmd.StandardOutput.ReadToEndAsync()}`");

		string? scriptPath = await SetupScript(client);
		if (scriptPath is null)
		{
			await Context.ModifyEphemeralResponseAsync("Typst failed to install!");
			return;
		}

		await Context.ModifyEphemeralResponseAsync($"Script found at `{scriptPath}`");
	}

#region Setup Script

	// ReSharper disable once InconsistentNaming
	private async Task<string?> SetupScript(HttpClient client)
	{
		string? latestCommitHash = await GetLatestCommitHash(client);
		if (latestCommitHash is null)
		{
			await Context.ModifyEphemeralResponseAsync("Failed to retrieve the latest commit hash of the Typst script!");
			return null;
		}

		string scriptDir = Path.Join(Program.TYPST_SCRIPT_DIR, $"{SCRIPTS_REPO_NAME}-{latestCommitHash}");
		if (!Directory.Exists(scriptDir))
		{
			await Context.ModifyEphemeralResponseAsync("Downloading script... (This will only happen once)");

			Directory.CreateDirectory(scriptDir);
			string zipPath = Path.Join(Program.TYPST_SCRIPT_DIR, $"{latestCommitHash}.zip");
			{
				await using Stream networkStream = await client.GetStreamAsync($"https://github.com/glyphs-fi/{SCRIPTS_REPO_NAME}/archive/{latestCommitHash}.zip");
				await using FileStream fileStream = new(zipPath, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
			}

			await Context.ModifyEphemeralResponseAsync("Extracting script zip... (This will only happen once)");
			await ExtractArchive(zipPath);
		}

		string scriptPath = Path.Join(scriptDir, "main.typ");
		return File.Exists(scriptPath) ? scriptPath : null;
	}

	[GeneratedRegex("""<meta\s+property=(["'])og:url\1\s+content=(["']).+/commit/(.+?)/?\2\s*/>""")]
	private static partial Regex HashRegex();

	// ReSharper disable once InconsistentNaming
	private static async Task<string?> GetLatestCommitHash(HttpClient client)
	{
		await using Stream networkStream = await client.GetStreamAsync($"https://github.com/glyphs-fi/{SCRIPTS_REPO_NAME}/commit/main");
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

		return null;
	}

#endregion

#region Setup Typst

	// ReSharper disable once InconsistentNaming
	private async Task<string?> SetupTypst(HttpClient client)
	{
		string? typstDownloadURL = GetTypstDownloadURLForPlatform();
		if (typstDownloadURL is null)
		{
			await Context.ModifyEphemeralResponseAsync("The server isn't running on a platform that this bot supports, so Typst cannot be installed...");
			return null;
		}

		string typstExeVersionDir = Path.Join(Program.TYPST_EXE_DIR, TYPST_VERSION);
		if (!Directory.Exists(typstExeVersionDir))
		{
			await Context.ModifyEphemeralResponseAsync("Downloading Typst... (This will only happen once)");

			Directory.CreateDirectory(typstExeVersionDir);
			string archivePath = Path.Join(typstExeVersionDir, Path.GetFileName(typstDownloadURL));
			{
				await using Stream networkStream = await client.GetStreamAsync(typstDownloadURL);
				await using FileStream fileStream = new(archivePath, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
			}

			await Context.ModifyEphemeralResponseAsync("Extracting Typst... (This will only happen once)");
			await ExtractArchive(archivePath);
		}

		return FindExe(typstExeVersionDir, "typst");
	}

	private const string URL = $"https://github.com/typst/typst/releases/download/{TYPST_VERSION}";
	private const string URL_LINUX_X64 = $"{URL}/typst-x86_64-unknown-linux-musl.tar.xz";
	private const string URL_LINUX_ARM64 = $"{URL}/typst-aarch64-unknown-linux-musl.tar.xz";
	private const string URL_WIN_X64 = $"{URL}/typst-x86_64-pc-windows-msvc.zip";

	private static string? GetTypstDownloadURLForPlatform()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			return RuntimeInformation.OSArchitecture switch
			{
				Architecture.X64 => URL_LINUX_X64,
				Architecture.Arm64 => URL_LINUX_ARM64,
				_ => null,
			};
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return RuntimeInformation.OSArchitecture switch
			{
				Architecture.X64 => URL_WIN_X64,
				_ => null,
			};
		}

		return null;
	}

#endregion

}
