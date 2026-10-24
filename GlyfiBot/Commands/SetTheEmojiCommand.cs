using JetBrains.Annotations;
using NetCord;
using NetCord.Gateway;
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
	private const string EMOJI_PARAMETER_DESCRIPTION = "Type a `:` and then the rest of the emoji. Let the autocomplete guide you!";
	private const string APPROVAL_EMOJI_NAME = "confirmed";

	public static async Task Init(GatewayClient client)
	{
		_submission.SetOnReact(client, OnSubmissionReact);
		_approvalEmoji = await FindApprovalEmoji(client);
		if (_approvalEmoji is null)
		{
			await Console.Error.WriteLineAsync($"""
			                                    WARNING: The bot does not have an :{APPROVAL_EMOJI_NAME}: emoji!
			                                    	Upload one here: https://discord.com/developers/applications/{client.Id}/emojis
			                                    """);
		}
	}

#region Submission

	private const string SUBMISSION_COMMAND_NAME = "submission";
	private static readonly InternalEmoji _submission = new(SUBMISSION_COMMAND_NAME);
	private static ReactionEmojiProperties? _approvalEmoji;

	[SubSlashCommand(SUBMISSION_COMMAND_NAME,
		"Set the emoji that will mark something as a submission in this channel")]
	[UsedImplicitly]
	public async Task Submission(
		[SlashCommandParameter(Description = EMOJI_PARAMETER_DESCRIPTION)]
		string? emoji = null)
	{
		await _submission.RunCommand(emoji, Context);
	}

	public static ReactionEmojiProperties? GetSubmissionEmoji(TextChannel channel)
	{
		return _submission.GetEmoji(channel);
	}

	private static async Task<ReactionEmojiProperties?> FindApprovalEmoji(GatewayClient client)
	{
		IReadOnlyList<ApplicationEmoji> applicationEmojis = await client.Rest.GetApplicationEmojisAsync(client.Id);
		ApplicationEmoji? approvalApplicationEmoji = applicationEmojis.FirstOrDefault(x => x.Name == APPROVAL_EMOJI_NAME);
		if (approvalApplicationEmoji == null) return null;
		return new ReactionEmojiProperties(approvalApplicationEmoji.Name, approvalApplicationEmoji.Id);
	}

	private static async Task OnSubmissionReact(ReactionEmojiProperties submissionEmoji, RestMessage message, RestGuild? guild)
	{
		ReactionEmojiProperties? disqualificationEmoji = GetDisqualificationEmoji(message.ChannelId);
		(bool valid, string? problem, _) = await SelectRangeCommand.ValidateMessage(message, submissionEmoji, guild, disqualificationEmoji);

		if (valid)
		{
			await message.AddReactionAsync(_approvalEmoji ?? new ReactionEmojiProperties("👍"));
			if (problem is not null)
			{
				await message.ReplyAsync($"Submission notice: {problem}"); //It's more of a warning here
			}
		}
		else
		{
			await message.ReplyAsync($"Submission failed: {problem}");
			await message.DeleteAllReactionsForEmojiAsync(submissionEmoji);
		}
	}

#endregion

#region Disqualification

	private const string DISQUALIFICATION_COMMAND_NAME = "disqualification";
	private static readonly InternalEmoji _disqualification = new(DISQUALIFICATION_COMMAND_NAME);

	[SubSlashCommand(DISQUALIFICATION_COMMAND_NAME,
		"Set the emoji that will mark something as disqualified in this channel")]
	[UsedImplicitly]
	public async Task Disqualification(
		[SlashCommandParameter(Description = EMOJI_PARAMETER_DESCRIPTION)]
		string? emoji = null)
	{
		await _disqualification.RunCommand(emoji, Context);
	}

	public static ReactionEmojiProperties? GetDisqualificationEmoji(TextChannel channel)
	{
		return _disqualification.GetEmoji(channel);
	}

	public static ReactionEmojiProperties? GetDisqualificationEmoji(ulong channelId)
	{
		return _disqualification.GetEmoji(channelId);
	}

#endregion

#region Internal

	private sealed class InternalEmoji
	{
		private readonly string _commandName;
		private readonly string _emojiFile;
		/// ChannelId, Emoji
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

		public void SetOnReact(GatewayClient client, Func<ReactionEmojiProperties, RestMessage, RestGuild?, Task> onReact)
		{
			client.MessageReactionAdd += ProcessReaction;
			return;

			async ValueTask ProcessReaction(MessageReactionAddEventArgs e)
			{
				if (e.UserId != e.MessageAuthorId) return;
				if (e.UserId == Program.BotUser.Id) return;
				if (!_emojis.TryGetValue(e.ChannelId, out ReactionEmojiProperties? emojiToLookFor)) return; // Does this channel have an emoji registered? (This is the "Correct channel?" check)
				if (_approvalEmoji is not null && _approvalEmoji.Is(e.Emoji))
				{
					// No-one else should be able to react with the approval emoji. Only the bot.
					await client.Rest.DeleteUserMessageReactionAsync(e.ChannelId, e.MessageId, _approvalEmoji, e.UserId);
					return;
				}

				if (!emojiToLookFor.Is(e.Emoji)) return;

				RestMessage message = await client.Rest.GetMessageAsync(e.ChannelId, e.MessageId);
				RestGuild? guild = e.GuildId.HasValue ? await client.Rest.GetGuildAsync(e.GuildId.Value) : null;
				await onReact.Invoke(emojiToLookFor, message, guild);
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

		public ReactionEmojiProperties? GetEmoji(ulong channelId)
		{
			return _emojis.GetValueOrDefault(channelId);
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

#endregion

}
