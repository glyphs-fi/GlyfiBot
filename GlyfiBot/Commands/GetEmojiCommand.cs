using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using JetBrains.Annotations;
using System.ComponentModel;
using System.Text;

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
			StringBuilder sb = new("Emoji has not been set!");
			sb.Append(context.Member.HasRole(SetTheRoleCommand.TheRole)
				? " Use `/set-emoji` to set the emoji before using `/select`."
				: " Contact an administrator.");
			await context.SendEphemeralResponse(sb.ToString());
		}
		else
		{
			await context.SendEphemeralResponse($"Emoji is set to {SetTheEmojiCommand.TheEmoji}");
		}
	}
}
