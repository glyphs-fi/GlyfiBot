using DSharpPlus.Commands;
using DSharpPlus.Entities;
using JetBrains.Annotations;
using System.ComponentModel;

namespace GlyfiBot.Commands;

public class SetEmojiCommand
{
	[Command("set")]
	[UsedImplicitly]
	public static async ValueTask ExecuteAsync(CommandContext context,
		[Description("Type a `:` and then the rest of the emoji. Let the autocomplete guide you!")]
		string emoji)
	{
		if (emoji == "null")
		{
			Program.TheEmoji = null;
			await context.SendEphemeralResponse($"Cleared emoji. Remember to `/set` it to something again before using `/select`!");
			return;
		}

		if (DiscordEmoji.TryFromUnicode(context.Client, emoji, out DiscordEmoji emojiReal) ||
		    DiscordEmoji.TryFromName(context.Client, emoji, out emojiReal) ||
		    DiscordEmoji.TryFromName(context.Client, $":{emoji}:", out emojiReal) ||
		    DiscordEmoji.TryFromName(context.Client, $":{emoji.TrimStart('<').TrimEnd('>').TrimStart('a').Trim(':').Split(":").FirstOrDefault("")}:", out emojiReal))
		{
			if (emojiReal.Id != 0 && !emojiReal.IsAvailable)
			{
				await context.SendEphemeralResponse($"Emoji {emojiReal} is not available...");
				return;
			}

			Program.TheEmoji = emojiReal;
			await context.SendEphemeralResponse($"Set emoji to {emojiReal}");
			return;
		}

		await context.SendEphemeralResponse($"Could not set emoji `{emoji}`");
	}
}
