using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Collections.Concurrent;
using System.Text.Json;

namespace GlyfiBot.Commands;

[SlashCommand("set-emoji",
	"Set the emoji that will mark something in this channel",
	DefaultGuildPermissions = Permissions.Administrator)]
public class SetTheEmojiCommand : ApplicationCommandModule<SlashCommandContext>
{
	private sealed class InternalEmoji
	{
		private readonly string _commandName;
		private readonly string _emojiFile;
		private readonly ConcurrentDictionary<ulong, ReactionEmojiProperties> _emojis;

		public InternalEmoji(string commandName)
		{
			_commandName = commandName;
			_emojiFile = $"{Program.SETTINGS_DIR}/emoji_{_commandName}.json";

			if (File.Exists(_emojiFile))
			{
				using FileStream fs = File.OpenRead(_emojiFile);
				Dictionary<ulong, string> dict = JsonSerializer.Deserialize(fs, ToJson.Default.DictionaryUInt64String)!;
				_emojis = new ConcurrentDictionary<ulong, ReactionEmojiProperties>(dict.Select(pair =>
				{
					string[] contents = pair.Value.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
					ReactionEmojiProperties theEmoji = contents.Length switch
					{
						1 => new ReactionEmojiProperties(contents[0]),
						2 => new ReactionEmojiProperties(contents[0], ulong.Parse(contents[1])),
						_ => throw new InvalidOperationException($"Could not parse {_commandName} emoji from {pair.Value}"),
					};

					return new KeyValuePair<ulong, ReactionEmojiProperties>(pair.Key, theEmoji);
				}));
			}
			else
			{
				_emojis = new ConcurrentDictionary<ulong, ReactionEmojiProperties>();
			}
		}

		public async Task RunCommand(string? emoji, SlashCommandContext context)
		{
			if (emoji is null || emoji.IsWhiteSpace())
			{
				RemoveEmojiRegistration(context);
				await context.SendEphemeralResponseAsync($"Cleared {_commandName} emoji for this channel. Remember to `/set-emoji {_commandName}` it to something again before using `/typst showcase` or `/select`!");
			}
			else
			{
				ReactionEmojiProperties setEmoji = AddEmojiRegistration(emoji, context);
				await context.SendEphemeralResponseAsync($"Set {_commandName} emoji for this channel to {setEmoji.Visual()}");
			}
			await Save();
		}

		private ReactionEmojiProperties AddEmojiRegistration(string emoji, SlashCommandContext context)
		{
			ulong channelId = context.Channel.Id;

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
				GuildEmoji? guildEmoji = context.Guild.GetEmojiByName(emoji);
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

		public ReactionEmojiProperties? GetEmoji(Channel channel)
		{
			return _emojis.GetValueOrDefault(channel.Id);
		}

		private void RemoveEmojiRegistration(SlashCommandContext context)
		{
			_emojis.TryRemove(context.Channel.Id, out _);
		}

		private async Task Save()
		{
			Dictionary<ulong, string> dict = _emojis.Select(pair => new KeyValuePair<ulong, string>(pair.Key, pair.Value.Name + ":" + pair.Value.Id)).ToDictionary();
			await using FileStream fs = File.Open(_emojiFile, FileMode.Create);
			await JsonSerializer.SerializeAsync(fs, dict, ToJson.Default.DictionaryUInt64String);
		}
	}

	private const string SUBMISSION_COMMAND_NAME = "submission";
	private static readonly InternalEmoji _submission = new(SUBMISSION_COMMAND_NAME);

	[SubSlashCommand(SUBMISSION_COMMAND_NAME,
		"Set the emoji that will mark something as a submission in this channel")]
	[UsedImplicitly]
	public async Task Submission(
		[SlashCommandParameter(Description = "Type a `:` and then the rest of the emoji. Let the autocomplete guide you!")]
		string? emoji = null)
	{
		await _submission.RunCommand(emoji, Context);
	}

	public static ReactionEmojiProperties? GetSubmissionEmoji(TextChannel channel)
	{
		return _submission.GetEmoji(channel);
	}
}
