using NetCord;
using NetCord.Gateway;

namespace GlyfiBot.Services;

public static class StatusChangerService
{
	/// <summary>
	/// Fill in whatever fun stuff you can think of, here! :)
	/// (They can't be localised by the way due to Discord limitations)
	/// </summary>
	private static readonly List<PotentialActivity> _potentialActivities =
	[
		new(BotActivityType.CompetingIn, "the Glyph Challenge"),
		new(BotActivityType.CompetingIn, "the Ambigram Challenge"),
		new(BotActivityType.Playing, "with their tittles"),
		new(BotActivityType.Streaming, "[IRL] Hunting for Glyphs!1!!(gone typosexual)"),
		new(BotActivityType.Watching, "obscure linguistic videos"),
		new(BotActivityType.ListeningTo, "wikipedia IPA sound examples"),
		new(BotActivityType.Playing, "\"dive into the rabbithole\" on Wikipedia"),
		new(BotActivityType.Watching, "Calligraphy ASMR"),
		new(BotActivityType.Playing, "with broad nibs"),
		new(BotActivityType.Playing, "with variable fonts"),
		new(BotActivityType.Playing, "with brushes"),
		new(BotActivityType.ListeningTo, "ABC songs of other languages"),
	];

	/// <summary>
	/// How long between the bot's status changes to a new random option from the list above ↑
	/// </summary>
	private static readonly TimeSpan _timeSpan = TimeSpan.FromMinutes(60);

	// ------------------------- Okay now there's no need to touch anything else below here. -------------------------

	private record PotentialActivity(BotActivityType Type, string Text);

	private enum BotActivityType
	{
		Playing,
		Streaming,
		ListeningTo,
		Watching,
		CompetingIn,
	}

	private static UserActivityType Convert(BotActivityType value) => value switch
	{
		BotActivityType.Playing => UserActivityType.Playing,
		BotActivityType.Streaming => UserActivityType.Streaming,
		BotActivityType.ListeningTo => UserActivityType.Listening,
		BotActivityType.Watching => UserActivityType.Watching,
		BotActivityType.CompetingIn => UserActivityType.Competing,
		_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
	};

	public static async Task RunAsync(GatewayClient client)
	{
		await SetRandomActivity(client); //initial

		using PeriodicTimer timer = new(_timeSpan);

		while(await timer.WaitForNextTickAsync())
		{
			try
			{
				await SetRandomActivity(client);
			}
			catch(Exception e)
			{
				Console.WriteLine(e);
				await Task.Delay(TimeSpan.FromSeconds(10)); // Some delay to prevent an exception from being thrown repeatedly
			}
		}
	}

	// ReSharper disable once InconsistentNaming
	private static async Task SetRandomActivity(GatewayClient client)
	{
		PotentialActivity botActivity = _potentialActivities[Random.Shared.Next(0, _potentialActivities.Count)];
		UserActivityProperties[] userActivity = [new(botActivity.Text, Convert(botActivity.Type))];
		await client.UpdatePresenceAsync(new PresenceProperties(UserStatusType.Online) {Activities = userActivity});
	}
}
