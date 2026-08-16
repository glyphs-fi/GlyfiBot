using System.Diagnostics;
using System.Text.RegularExpressions;
using static GlyfiBot.Utils;

namespace GlyfiBot.Services;

public static partial class FfmpegService
{
	private const string FFMPEG_TAG = "autobuild-2026-08-15-13-02";
	private const string FFMPEG_VERSION = "n8.1.2-44-g7c533d0f86";
	private const string FFMPEG_HASH_LINUX_X64 = "01f4a27f58acbea7c484232a62a7381f716ddf000e8f311b791f8bfc7c8912aa";
	private const string FFMPEG_HASH_LINUX_ARM64 = "4951fa5b7e29deaf485d09f699acf3cc4b4030dc5e4e7f5f248438f77873a2a4";
	private const string FFMPEG_HASH_WINDOWS_X64 = "0e7829b6e1ba867e37bbad17153de258bd3bffaa3b745626a6424df0ea113970";

	public static FfmpegRunner? Ffmpeg { get; private set; }

	public static async Task RunAsync()
	{
		try
		{
			await SetupFfmpeg();
		}
		catch(Exception e)
		{
			Console.Error.WriteLine(e);
		}
	}

	public class FfmpegRunner(string ffmpegExecutable)
	{
		public async Task Run(params List<string> arguments)
		{
			await RunFfmpegCommand(ffmpegExecutable, arguments);
		}
	}

	private static async Task SetupFfmpeg()
	{
		try
		{
			Process ffmpegCmd = await RunFfmpegCommand("ffmpeg", ["-version"]);
			if (ffmpegCmd.ExitCode != 0)
			{
				throw new InvalidOperationException($"""
				                                     ffmpeg presence (version) check exited with: {ffmpegCmd.ExitCode}

				                                     --- stderr: ---
				                                     {await ffmpegCmd.StandardError.ReadToEndAsync()}

				                                     --- stdout: ---
				                                     {await ffmpegCmd.StandardOutput.ReadToEndAsync()}
				                                     """);
			}

			Console.WriteLine("Using system ffmpeg!");
			Ffmpeg = new FfmpegRunner("ffmpeg");
		}
		catch(System.ComponentModel.Win32Exception)
		{
			Ffmpeg = null;
			Console.WriteLine("Could not find ffmpeg installed. Using downloaded version instead...");
			string ffmpegExe = await DownloadFfmpeg();
			Ffmpeg = new FfmpegRunner(ffmpegExe);
		}
		catch(InvalidOperationException)
		{
			Console.WriteLine("System ffmpeg is not usable. Using downloaded version instead...");
			string ffmpegExe = await DownloadFfmpeg();
			Ffmpeg = new FfmpegRunner(ffmpegExe);
		}
	}

	private static async Task<Process> RunFfmpegCommand(string executable, List<string> arguments)
	{
		ProcessStartInfo startInfo = new(executable, arguments) {RedirectStandardOutput = true, RedirectStandardError = true};
		Process process = new() {StartInfo = startInfo};
		process.Start();
		await process.WaitForExitAsync();

		return process;
	}

	/// <returns>The path to the ffmpeg executable (the file itself, not the containing directory)</returns>
	private static async Task<string> DownloadFfmpeg()
	{
		string ffmpegVersionShort = GetShortFfmpegVersion().Match(FFMPEG_VERSION).Groups[1].Value;
		string filename = SwitchOnPlatformArch(
			linuxX64: $"ffmpeg-{FFMPEG_VERSION}-linux64-gpl-{ffmpegVersionShort}.tar.xz",
			linuxArm64: $"ffmpeg-{FFMPEG_VERSION}-linuxarm64-gpl-{ffmpegVersionShort}.tar.xz",
			winX64: $"ffmpeg-{FFMPEG_VERSION}-win64-gpl-{ffmpegVersionShort}.zip"
		);
		string hardcodedHash = SwitchOnPlatformArch(
			linuxX64: FFMPEG_HASH_LINUX_X64,
			linuxArm64: FFMPEG_HASH_LINUX_ARM64,
			winX64: FFMPEG_HASH_WINDOWS_X64
		);

		(string ffmpegDownloadUrl, string remoteHash) = await GetReleaseAsset("BtbN", "FFmpeg-Builds", FFMPEG_TAG, filename, ifReleaseNotMutableThenProvideHash: hardcodedHash);

		string ffmpegExeVersionDir = Path.Join(Program.FFMPEG_EXE_DIR, FFMPEG_VERSION);
		if (!Directory.Exists(ffmpegExeVersionDir) || DirectoryEmpty(ffmpegExeVersionDir))
		{
			Console.WriteLine("Downloading ffmpeg...");

			Directory.CreateDirectory(ffmpegExeVersionDir);
			string archivePath = Path.Join(ffmpegExeVersionDir, filename);
			{
				await using Stream networkStream = await Program.HttpClient.GetStreamAsync(ffmpegDownloadUrl);
				await using FileStream fileStream = new(archivePath, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
			}

			string localHash = await HashFile(archivePath);
			if (!string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
			{
				File.Delete(archivePath);
				Directory.Delete(ffmpegExeVersionDir);
				throw new PlatformNotSupportedException($"Failed to verify the ffmpeg download!\nLocal hash `{localHash.ToLower()}` did not match remote hash `{remoteHash.ToLower()}`");
			}

			Console.WriteLine("Extracting ffmpeg...");
			await ExtractArchive(archivePath);
		}

		string? exeLocation = FindExe(ffmpegExeVersionDir, "ffmpeg");
		if (exeLocation == null) throw new FileNotFoundException("Could not find a ffmpeg executable in the unpacked archive!");

		Console.WriteLine("Downloaded ffmpeg is ready!");
		return exeLocation;
	}

	[GeneratedRegex(@"^n(\d+\.\d+)")]
	private static partial Regex GetShortFfmpegVersion();
}
