using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Text.Json;

namespace GlyfiBot.Commands;

public class SetTheEmojiCommand : ApplicationCommandModule<SlashCommandContext>
{
	private const string EMOJI_FILE = $"{Program.SETTINGS_DIR}/emoji.json";

	private static Dictionary<ulong, ReactionEmojiProperties> _emojis = null!;

	public static ReactionEmojiProperties? GetEmoji(Channel channel)
	{
		return _emojis.GetValueOrDefault(channel.Id);
	}

	public static async Task Load()
	{
		if (File.Exists(EMOJI_FILE))
		{
			await using FileStream fs = File.OpenRead(EMOJI_FILE);
			Dictionary<ulong, string> dict = (await JsonSerializer.DeserializeAsync(fs, ToJson.Default.DictionaryUInt64String))!;
			_emojis = dict.Select(pair =>
			{
				string[] contents = pair.Value.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
				ReactionEmojiProperties theEmoji = contents.Length switch
				{
					1 => new ReactionEmojiProperties(contents[0]),
					2 => new ReactionEmojiProperties(contents[0], ulong.Parse(contents[1])),
					_ => throw new InvalidOperationException($"Could not parse emoji from {pair.Value}"),
				};

				return new KeyValuePair<ulong, ReactionEmojiProperties>(pair.Key, theEmoji);
			}).ToDictionary();
		}
		else
		{
			_emojis = new Dictionary<ulong, ReactionEmojiProperties>();
		}
	}

	[SlashCommand("set-emoji",
		"Set the emoji that will mark something as a submission in this channel",
		DefaultGuildPermissions = Permissions.Administrator)]
	[UsedImplicitly]
	public async Task ExecuteAsync(
		[SlashCommandParameter(Description = "Type a `:` and then the rest of the emoji. Let the autocomplete guide you!")]
		string? emoji = null)
	{
		if (emoji is null || emoji.IsWhiteSpace())
		{
			RemoveEmojiRegistration();
			await Context.SendEphemeralResponseAsync("Cleared emoji for this channel. Remember to `/set-emoji` it to something again before using `/typst showcase` or `/select`!");
		}
		else
		{
			ReactionEmojiProperties setEmoji = AddEmojiRegistration(emoji);
			await Context.SendEphemeralResponseAsync($"Set emoji for this channel to {setEmoji.Visual()}");
		}
		await SaveEmoji();
	}

	private ReactionEmojiProperties AddEmojiRegistration(string emoji)
	{
		ulong channelId = Context.Channel.Id;

		ReactionEmojiProperties setEmoji;
		if (emoji.Contains(':'))
		{
			try
			{
				string emojiClean = emoji.TrimStart('<').TrimEnd('>').TrimStart('a').Trim(':');
				string[] parts = emojiClean.Split(":");
				string name = parts[0];
				ulong id = ulong.Parse(parts[1]);
				setEmoji = new ReactionEmojiProperties(name, id);
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
				setEmoji = new ReactionEmojiProperties(guildEmoji.Name, guildEmoji.Id);
			}
			else
			{
				string? firstEmoji = Utils.FirstEmoji(emoji);
				if (firstEmoji is null)
				{
					throw new SimpleCommandFailException($"Invalid emoji: `{emoji}`");
				}

				setEmoji = new ReactionEmojiProperties(firstEmoji);
			}
		}

		return _emojis[channelId] = setEmoji;
	}

	private void RemoveEmojiRegistration()
	{
		_emojis.Remove(Context.Channel.Id);
	}

	private static async Task SaveEmoji()
	{
		Dictionary<ulong, string> dict = _emojis.Select(pair => new KeyValuePair<ulong, string>(pair.Key, pair.Value.Name + ":" + pair.Value.Id)).ToDictionary();
		await using FileStream fs = File.OpenWrite(EMOJI_FILE);
		await JsonSerializer.SerializeAsync(fs, dict, ToJson.Default.DictionaryUInt64String);
	}
}
