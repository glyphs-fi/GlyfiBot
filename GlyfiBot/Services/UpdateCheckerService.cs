using NetCord;
using NetCord.Gateway;
using static GlyfiBot.Utils;

namespace GlyfiBot.Services;

public static class UpdateCheckerService
{
	/// How long between update checks
	private static readonly TimeSpan _timeSpan = TimeSpan.FromHours(24);

	public static async Task RunAsync(GatewayClient client)
	{
		HashSet<ulong> userIdsToNotifyOfUpdate = await FindUserIds(client);

		await NotifyUsers(client, userIdsToNotifyOfUpdate); //initial

		using PeriodicTimer timer = new(_timeSpan);

		while(await timer.WaitForNextTickAsync())
		{
			try
			{
				await NotifyUsers(client, userIdsToNotifyOfUpdate);
			}
			catch(Exception e)
			{
				Console.WriteLine(e);
				await Task.Delay(TimeSpan.FromSeconds(10)); // Some delay to prevent an exception from being thrown repeatedly
			}
		}
	}

	private static async Task NotifyUsers(GatewayClient client, HashSet<ulong> userIdsToNotifyOfUpdate)
	{
		string latestCommitHash = await GetLatestCommitHash("GlyfiBot");
		string? executableHash = ExecutableGitHash();

		if (string.Equals(executableHash, latestCommitHash, StringComparison.OrdinalIgnoreCase)) return;

		foreach(ulong userId in userIdsToNotifyOfUpdate)
		{
			DMChannel dmChannel = await client.Rest.GetDMChannelAsync(userId);
			await dmChannel.SendMessageAsync($"""
			                                  I need to be updated!

			                                  **Latest version:**  `{latestCommitHash}`
			                                  **Current version:**  `{executableHash}`

			                                  **Download here:**  <https://github.com/glyphs-fi/GlyfiBot/actions/workflows/build.yml?query=branch%3Amain>
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
