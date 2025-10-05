using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using JetBrains.Annotations;
using System.ComponentModel;

namespace GlyfiBot.Commands;

public class GetEmojiCommand
{
	[Command("emoji")]
	[Description("Which emoji do I need to use to mark something as a submission?")]
	[UsedImplicitly]
	public static async ValueTask ExecuteAsync(SlashCommandContext context)
	{
		if (SetTheEmojiCommand.TheEmoji is null)
		{
			await context.SendEphemeralResponse("Emoji has not been set!");
		}
		else
		{
			await context.SendEphemeralResponse($"Emoji is set to {SetTheEmojiCommand.TheEmoji}");
		}
	}
}
