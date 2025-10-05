using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Net.Serialization;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;
using System.ComponentModel;

namespace GlyfiBot.Commands;

public class SetTheEmojiCommand
{
	private const string EMOJI_FILE = $"{Program.SETTINGS_DIR}/emoji.json";

	private static DiscordEmoji? _theEmojiBacking = null;

	public static DiscordEmoji? TheEmoji
	{
		get => _theEmojiBacking;
		private set
		{
			if (value is null)
			{
				File.Delete(EMOJI_FILE);
			}
			else
			{
				File.WriteAllText(EMOJI_FILE, DiscordJson.SerializeObject(value));
			}
			_theEmojiBacking = value;
		}
	}

	public static void Load()
	{
		if (File.Exists(EMOJI_FILE))
		{
			string jsonString = File.ReadAllText(EMOJI_FILE);
			JToken jToken = JToken.Parse(jsonString);
			TheEmoji = jToken.ToDiscordObject<DiscordEmoji>();
			Console.WriteLine($"Loaded emoji to {TheEmoji}");
		}
		else
		{
			Console.WriteLine("Emoji has not been set.");
		}
	}

	[Command("set-emoji")]
	[Description("Set the emoji that will mark something as a submission")]
	[RequirePermissions([], [DiscordPermission.Administrator])]
	[UsedImplicitly]
	public static async ValueTask EmojiAsync(SlashCommandContext context,
		[Description("Type a `:` and then the rest of the emoji. Let the autocomplete guide you!")]
		string emoji)
	{
		if (emoji is "null" or "clear" or "empty" or "nothing")
		{
			TheEmoji = null;
			await context.SendEphemeralResponse("Cleared emoji. Remember to `/set-emoji` it to something again before using `/select`!");
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

			TheEmoji = emojiReal;
			string message = $"Set emoji to {emojiReal}";
			Console.WriteLine(message);
			await context.SendEphemeralResponse(message);
			return;
		}

		await context.SendEphemeralResponse($"Could not set emoji `{emoji}`");
	}
}
