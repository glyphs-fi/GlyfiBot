using JetBrains.Annotations;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace GlyfiBot.Commands;

public class GetTheEmojiCommand : ApplicationCommandModule<SlashCommandContext>
{
	[SlashCommand("emoji",
		"Which emoji do I need to use to mark something as a submission in this channel?")]
	[UsedImplicitly]
	public async Task ExecuteAsync()
	{
		ReactionEmojiProperties? theEmoji = SetTheEmojiCommand.GetEmoji(Context.Channel);
		if (theEmoji is null)
		{
			await Context.SendEphemeralResponseAsync("Emoji has not been set for this channel!");
		}
		else
		{
			await Context.SendEphemeralResponseAsync($"Emoji in this channel is set to {theEmoji.Visual()}");
		}
	}
}
