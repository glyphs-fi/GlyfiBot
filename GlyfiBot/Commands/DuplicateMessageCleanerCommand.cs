using GlyfiBot.Services;
using JetBrains.Annotations;
using NetCord;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;

namespace GlyfiBot.Commands;

public class DuplicateMessageCleanerCommand : ApplicationCommandModule<SlashCommandContext>
{
	[SlashCommand("set-dupe-notif-channel",
		"Set up in which channel the Moderation Notifications for the Duplicate Message Cleaner will be sent",
		DefaultGuildPermissions = Permissions.Administrator)]
	[UsedImplicitly]
	public async Task ExecuteAsync(
		[SlashCommandParameter(Description = "The ID of the channel")]
		string? channelId = null)
	{
		Guild? guild = Context.Guild;
		if (guild is null)
		{
			await Context.SendEphemeralResponseAsync("Can only be run from in a server.");
			return;
		}

		if (channelId is null || channelId.IsWhiteSpace())
		{
			DuplicateMessageCleanerService.RemoveNotificationChannel(guild);
			await Context.SendEphemeralResponseAsync("""
			                                         Cleared the Moderation Notifications Channel for the Duplicate Message Cleaner for this server.
			                                         Remember to `/set-dupe-notif-channel` it to something again before another spammer comes along!
			                                         """);
		}
		else
		{
			if (!ulong.TryParse(channelId, null, out ulong parsedChannelId))
			{
				throw new SimpleCommandFailException("`channelId` needs to be a number: the Channel ID");
			}
			Channel channel = await Context.Client.Rest.GetChannelAsync(parsedChannelId);
			if (channel is not TextGuildChannel textGuildChannel)
			{
				throw new SimpleCommandFailException("`channelId` does not point to a valid Server Text Channel");
			}
			DuplicateMessageCleanerService.SetModNotificationChannel(guild, textGuildChannel);
			await Context.SendEphemeralResponseAsync($"Set the Moderation Notifications Channel for the Duplicate Message Cleaner for this server to {textGuildChannel}");
		}
	}
}
