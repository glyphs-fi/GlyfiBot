using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static GlyfiBot.Utils;

namespace GlyfiBot.Commands;

public class TypstCommand : ApplicationCommandModule<SlashCommandContext>
{
	private const string TYPST_VERSION = "v0.14.2";

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
			await ModifyResponseAsync(msg =>
			{
				msg.Flags = MessageFlags.Ephemeral;
				msg.Content = "Typst failed to install!";
			});
			return;
		}

		await ModifyResponseAsync(msg =>
		{
			msg.Flags = MessageFlags.Ephemeral;
			msg.Content = $"Typst found at `{typstExe}`";
		});

		Process typstCmd = new() {StartInfo =  new ProcessStartInfo(typstExe, ["--version"]) {RedirectStandardOutput = true}};
		typstCmd.Start();
		await typstCmd.WaitForExitAsync();

		await ModifyResponseAsync(msg =>
		{
			msg.Flags = MessageFlags.Ephemeral;
			msg.Content = $"Typst found at `{typstExe}` with version `{typstCmd.StandardOutput.ReadToEnd()}`";
		});
	}

#region Setup Typst

	// ReSharper disable once InconsistentNaming
	private async Task<string?> SetupTypst(HttpClient client)
	{
		string? typstDownloadURL = GetTypstDownloadURLForPlatform();
		if (typstDownloadURL is null)
		{
			await Context.SendEphemeralResponseAsync("The server isn't running on a platform that this bot supports, so Typst cannot be installed...");
			return null;
		}

		string typstExeVersionDir = Path.Join(Program.TYPST_EXE_DIR, TYPST_VERSION);
		if (!Directory.Exists(typstExeVersionDir))
		{
			await ModifyResponseAsync(msg =>
			{
				msg.Flags = MessageFlags.Ephemeral;
				msg.Content = "Downloading Typst... (This will only happen once)";
			});

			Directory.CreateDirectory(typstExeVersionDir);
			string archivePath = Path.Join(typstExeVersionDir, typstDownloadURL.Split('/').Last());
			{
				await using Stream networkStream = await client.GetStreamAsync(typstDownloadURL);
				await using FileStream fileStream = new(archivePath, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
			}

			await ModifyResponseAsync(msg =>
			{
				msg.Flags = MessageFlags.Ephemeral;
				msg.Content = "Extracting Typst... (This will only happen once)";
			});
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
