using JetBrains.Annotations;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace GlyfiBot.Commands;

public class GetTheEmojiCommand : ApplicationCommandModule<SlashCommandContext>
{
	[SlashCommand("emoji",
		"Which emoji do I need to use to mark something as a submission?")]
	[UsedImplicitly]
	public async Task ExecuteAsync()
	{
		ReactionEmojiProperties? theEmoji = SetTheEmojiCommand.TheEmoji;
		if (theEmoji is null)
		{
			await Context.SendEphemeralResponseAsync("Emoji has not been set!");
		}
		else
		{
			await Context.SendEphemeralResponseAsync($"Emoji is set to {theEmoji.String()}");
		}
	}
}
