using NetCord;
using NetCord.Gateway;
using static GlyfiBot.Utils;

namespace GlyfiBot.Services;

public static class UpdateCheckerService
{
	/// How long between update checks
	private static readonly TimeSpan _timeSpan = TimeSpan.FromHours(24);

	private const string REPO_NAME = "GlyfiBot";

	public static async Task RunAsync(GatewayClient client)
	{
		try
		{
			// Clean up the previous update
			if (Directory.Exists(Program.UPDATE_AVAILABLE_DIR)) Directory.Delete(Program.UPDATE_AVAILABLE_DIR, true);

			// Initial update check, on startup
			await CheckForUpdate(client);
		}
		catch(Exception e)
		{
			Console.Error.WriteLine(e);
		}

		using PeriodicTimer timer = new(_timeSpan);

		while(await timer.WaitForNextTickAsync())
		{
			try
			{
				await CheckForUpdate(client);
			}
			catch(Exception e)
			{
				Console.Error.WriteLine(e);
				await Task.Delay(TimeSpan.FromSeconds(10)); // Some delay to prevent an exception from being thrown repeatedly
			}
		}
	}

	private static async Task CheckForUpdate(GatewayClient client)
	{
		string latestReleaseTag = await GetLatestRelease(REPO_NAME);
		string latestReleaseHash = VersionRegex().Replace(latestReleaseTag, "");
		string? executableHash = ExecutableGitHash();

		// Actual check
		if (string.Equals(executableHash, latestReleaseHash, StringComparison.OrdinalIgnoreCase)) return;

		// An update is available!
		string filename = SwitchOnPlatformArch(
			linuxX64: "GlyfiBot_linux-x64.zip",
			linuxArm64: "GlyfiBot_linux-arm64.zip",
			winX64: "GlyfiBot_win-x64.zip"
		);
		(string updateDownloadUrl, string remoteHash) = await GetReleaseAsset("glyphs-fi", REPO_NAME, latestReleaseTag, filename);

		string updateVersionDir = Path.Join(Program.UPDATES_STAGING_DIR, latestReleaseHash);
		if (Directory.Exists(updateVersionDir)) Directory.Delete(updateVersionDir, true);

		Console.WriteLine("Downloading update...");

		Directory.CreateDirectory(updateVersionDir);
		string archivePath = Path.Join(updateVersionDir, filename);
		{
			await using Stream networkStream = await Program.HttpClient.GetStreamAsync(updateDownloadUrl);
			await using FileStream fileStream = new(archivePath, FileMode.CreateNew);
			await networkStream.CopyToAsync(fileStream);
		}

		string localHash = await HashFile(archivePath);
		if (!string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
		{
			File.Delete(archivePath);
			Directory.Delete(updateVersionDir);
			throw new PlatformNotSupportedException($"Failed to verify the update download!\nLocal hash `{localHash.ToLower()}` did not match remote hash `{remoteHash.ToLower()}`");
		}

		Console.WriteLine("Extracting update...");
		await ExtractArchive(archivePath);
		File.Delete(archivePath);

		Console.WriteLine("Update downloaded! Staging...");

		// Clean up the previous update
		if (Directory.Exists(Program.UPDATE_AVAILABLE_DIR)) Directory.Delete(Program.UPDATE_AVAILABLE_DIR, true);

		Directory.Move(updateVersionDir, Program.UPDATE_AVAILABLE_DIR);

		Console.WriteLine("Update staged and ready to be installed!");

		await NotifyUsers(client, latestReleaseHash, executableHash);
	}

	private static async Task NotifyUsers(GatewayClient client, string latestReleaseHash, string? executableHash)
	{
		HashSet<ulong> userIdsToNotifyOfUpdate = await FindUserIds(client);
		foreach(ulong userId in userIdsToNotifyOfUpdate)
		{
			DMChannel dmChannel = await client.Rest.GetDMChannelAsync(userId);
			await dmChannel.SendMessageAsync($"""
			                                  I need to be updated!

			                                  _Luckily, I have already downloaded the update for you!_
			                                  Please look in the folder where I'm running, and you'll find a folder called `{Program.UPDATE_AVAILABLE_DIR}`.
			                                  In there, are the new executables for me :)

			                                  So please shut me down, and replace my files with these new ones!

			                                  > **Current version:**  `{executableHash}`
			                                  > **Latest version:**  `{latestReleaseHash}`
			                                  > **Changelog:** <https://github.com/glyphs-fi/GlyfiBot/releases/v_{latestReleaseHash}>
			                                  """);
		}
	}

	private static async Task<HashSet<ulong>> FindUserIds(GatewayClient client)
	{
		HashSet<ulong> userIdsToNotifyOfUpdate = [];

		CurrentApplication application = await client.Rest.GetCurrentApplicationAsync();

		User? owningUser = application.Owner;
		if (owningUser is not null)
		{
			userIdsToNotifyOfUpdate.Add(owningUser.Id);
		}
		else
		{
			// If the bot does not have a single owner, it probably has a team as owner instead
			Team? owningTeam = application.Team;
			if (owningTeam is not null)
			{
				User teamOwner = await client.Rest.GetUserAsync(owningTeam.OwnerId);
				userIdsToNotifyOfUpdate.Add(teamOwner.Id);
				IEnumerable<TeamUser> teamUsers = owningTeam.Users //
					.Where(user => user.MembershipState is MembershipState.Accepted) //
					.Where(user => user.Role is TeamRole.Admin or TeamRole.Developer);

				foreach(TeamUser teamUser in teamUsers) userIdsToNotifyOfUpdate.Add(teamUser.Id);

			}
		}

		return userIdsToNotifyOfUpdate;
	}
}
