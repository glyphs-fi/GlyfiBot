using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace GlyfiBot.Commands;

public class SetTheEmojiCommand : ApplicationCommandModule<SlashCommandContext>
{
	private const string EMOJI_FILE = $"{Program.SETTINGS_DIR}/emoji.txt";

	public static ReactionEmojiProperties? TheEmoji
	{
		get;
		private set
		{
			if (value is null)
			{
				File.Delete(EMOJI_FILE);
			}
			else
			{
				File.WriteAllText(EMOJI_FILE, value.Name + "\n" + value.Id);
			}
			field = value;
		}
	} = null;

	public static void Load()
	{
		if (File.Exists(EMOJI_FILE))
		{
			string[] contents = File.ReadAllLines(EMOJI_FILE);
			TheEmoji = contents.Length switch
			{
				1 => new ReactionEmojiProperties(contents[0]),
				2 => new ReactionEmojiProperties(contents[0], ulong.Parse(contents[1])),
				_ => null,
			};
			if (TheEmoji is not null)
				Console.WriteLine($"Loaded emoji to {TheEmoji.String()}");
		}
		else
		{
			Console.WriteLine("Emoji has not been set.");
		}
	}

	[SlashCommand("set-emoji",
		"Set the emoji that will mark something as a submission",
		DefaultGuildPermissions = Permissions.Administrator)]
	[UsedImplicitly]
	public async Task ExecuteAsync(
		[SlashCommandParameter(Description = "Type a `:` and then the rest of the emoji. Let the autocomplete guide you!")]
		string? emoji = null)
	{
		if (emoji is null)
		{
			TheEmoji = null;
			throw new SimpleCommandFailException("Cleared emoji. Remember to `/set-emoji` it to something again before using `/select`!");
		}

		if (emoji.Contains(':'))
		{
			try
			{
				string emojiClean = emoji.TrimStart('<').TrimEnd('>').TrimStart('a').Trim(':');
				string[] parts = emojiClean.Split(":");
				string name = parts[0];
				ulong id = ulong.Parse(parts[1]);
				TheEmoji = new ReactionEmojiProperties(name, id);
			}
			catch(SystemException e) when(e is IndexOutOfRangeException or FormatException)
			{
				throw new SimpleCommandFailException($"Invalid emoji: `{emoji}`");
			}
		}
		else
		{
			GuildEmoji? guildEmoji = Context.Guild.GetEmojiByName(emoji);
			if (guildEmoji is not null)
			{
				TheEmoji = new ReactionEmojiProperties(guildEmoji.Name, guildEmoji.Id);
			}
			else
			{
				string? firstEmoji = Utils.FirstEmoji(emoji);
				if (firstEmoji is null)
				{
					throw new SimpleCommandFailException($"Invalid emoji: `{emoji}`");
				}

				TheEmoji = new ReactionEmojiProperties(firstEmoji);
			}
		}

		await Context.SendEphemeralResponseAsync($"Set emoji to {TheEmoji.String()}");
	}
}
