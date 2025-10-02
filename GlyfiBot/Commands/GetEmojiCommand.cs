using DSharpPlus.Commands;
using JetBrains.Annotations;

namespace GlyfiBot.Commands;

public class GetEmojiCommand
{
	[Command("get")]
	[UsedImplicitly]
	public static async ValueTask ExecuteAsync(CommandContext context)
	{
		if (Program.TheEmoji is null)
		{
			await context.SendEphemeralResponse("Emoji has not been set! Use `/set` to set the emoji before using `/select`.");
		}
		else
		{
			await context.SendEphemeralResponse($"Emoji is set to {Program.TheEmoji}");
		}
	}
}
