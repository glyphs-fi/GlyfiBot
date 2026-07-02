using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace GlyfiBot.Services;

public static class DuplicateMessageCleanerService
{
	private const string MOD_CHANNELS_FILE = $"{Program.SETTINGS_DIR}/duplicate_message_cleaner_mod_channels.json";

	/// <summary>
	/// Allowed time between duplicate messages.
	/// If a duplicate message is sent within this time, both are deleted.
	/// If this time has passed, neither are touched.
	/// </summary>
	// ReSharper disable once InconsistentNaming
	private static readonly TimeSpan COOLDOWN = TimeSpan.FromMinutes(10);

	/// <summary>
	/// If a duplicate message gets removed, the author gets timed out for this many minutes:
	/// </summary>
	private const int TIMEOUT_TIME_MINUTES = 60;

	private const string BUTTON_ACTION_BAN = "button_ban";
	private const string BUTTON_ACTION_REMOVE_TIMEOUT = "button_remove-timeout";
	private const string MODAL_BAN = "modal_ban";
	private const string MODAL_BAN_DELETE_RADIOS = "modal_ban-delete-radios";

	private static ConcurrentDictionary<ulong, ulong> _modChannels = null!;

	private static readonly ConcurrentDictionary<ulong, Message> _userMessages = new();

	private static GatewayClient _client = null!;
	private static ulong _botUserId;

	public static async Task RunAsync(GatewayClient client)
	{
		_client = client;
		_botUserId = (await _client.Rest.GetCurrentUserAsync()).Id;

		if (File.Exists(MOD_CHANNELS_FILE))
		{
			await using FileStream fs = File.OpenRead(MOD_CHANNELS_FILE);
			Dictionary<ulong, ulong> dict = (await JsonSerializer.DeserializeAsync(fs, ToJson.Default.DictionaryUInt64UInt64))!;
			_modChannels = new ConcurrentDictionary<ulong, ulong>(dict);
		}
		else
		{
			_modChannels = new ConcurrentDictionary<ulong, ulong>();
		}

		_client.MessageCreate += ProcessMessage;
		_client.InteractionCreate += ProcessInteraction;
	}

	public static void RemoveNotificationChannel(Guild guild)
	{
		_modChannels.TryRemove(guild.Id, out ulong _);
		SaveModChannels();
	}

	public static void SetModNotificationChannel(Guild guild, TextGuildChannel channel)
	{
		_modChannels[guild.Id] = channel.Id;
		SaveModChannels();
	}

	private static async ValueTask ProcessMessage(Message thisMessage)
	{
		ulong author = thisMessage.Author.Id;

		// Do not check own bot messages
		if (author == _botUserId) return;

		// Do not check in non-guild areas
		if (thisMessage.Author is not GuildUser guildUser) return;

		// Do not check messages from people who have permissions to delete messages (they're probably admins/mods, who don't need spam checking)
		if (thisMessage.Guild is null) return;
		Permissions permissions = guildUser.GetPermissions(thisMessage.Guild);
		if (permissions.HasFlag(Permissions.ManageMessages)) return;

		// Is this user's previous message loaded?
		if (_userMessages.TryGetValue(author, out Message? prevMessage))
		{
			// If there isn't enough time between this new message and the previous one...
			TimeSpan diff = thisMessage.CreatedAt - prevMessage.CreatedAt;
			if (diff < COOLDOWN)
			{
				// ...then we compare contents (including attachments!)
				string thisContent = GetContentFromMessage(thisMessage);
				string prevContent = GetContentFromMessage(prevMessage);
				if (thisContent == prevContent)
				{
					// If they are the same, then we stop the spam!

					// First we timeout (to prevent further infractions) and we let the mods know
					await Task.WhenAll([
						TimeoutUser(guildUser),
						NotifyMods(prevMessage, thisMessage),
					]);

					// Lastly, we clean up the mess
					await Task.WhenAll([
						DeleteMessageIfExists(prevMessage),
						DeleteMessageIfExists(thisMessage),
					]);
				}
			}
			// Finally, we update the user's previous message
			_userMessages.TryUpdate(author, thisMessage, prevMessage);
		}
		else
		{
			// Add this message to the bot's memory
			_userMessages.TryAdd(author, thisMessage);
		}
	}

	private static async ValueTask ProcessInteraction(Interaction interaction)
	{
		switch(interaction)
		{
			case ButtonInteraction buttonInteraction:
				await ProcessButtonInteraction(buttonInteraction);
				break;
			case ModalInteraction modalInteraction:
				await ProcessModalInteraction(modalInteraction);
				break;
		}
	}

	private static async ValueTask ProcessButtonInteraction(ButtonInteraction interaction)
	{
		Guild? guild = interaction.Guild;
		if (guild is null) return;

		if (interaction.User is not GuildUser guildUser) return;

		InteractionDataContainer<ulong> interactionData = new(interaction.Data.CustomId);
		if (interactionData.Source != nameof(DuplicateMessageCleanerService)) return;

		string buttonAction = interactionData.Type;
		ulong affectedUserId = interactionData.Extra;
		Permissions permissions = guildUser.GetPermissions(guild);

		switch(buttonAction)
		{
			case BUTTON_ACTION_BAN:
				if (!permissions.HasFlag(Permissions.BanUsers))
				{
					await interaction.SendResponseAsync(InteractionCallback.Message($"You do not have permission to ban users, {guildUser}!"));
					return;
				}
				await interaction.SendResponseAsync(InteractionCallback.Modal(new ModalProperties(new InteractionDataContainer<ulong>(
						nameof(DuplicateMessageCleanerService),
						MODAL_BAN,
						affectedUserId
					).ToString(),
					"Ban",
					[
						new LabelProperties("Delete more messages?",
							new RadioGroupProperties(MODAL_BAN_DELETE_RADIOS, [
								new RadioGroupOptionProperties("Don't Delete Any", "0"),
								new RadioGroupOptionProperties("Previous Hour", "1"),
								new RadioGroupOptionProperties("Previous 6 Hours", "6"),
								new RadioGroupOptionProperties("Previous 12 Hours", "12"),
								new RadioGroupOptionProperties("Previous 24 Hours", "24"),
								new RadioGroupOptionProperties("Previous 3 Days", "72"),
								new RadioGroupOptionProperties("Previous 7 Days", "168"),
							])
						),
					])));
				break;
			case BUTTON_ACTION_REMOVE_TIMEOUT:
				if (!permissions.HasFlag(Permissions.ModerateUsers))
				{
					await interaction.SendResponseAsync(InteractionCallback.Message($"You do not have permission to remove timeouts, {guildUser}!"));
					return;
				}
				await _client.Rest.ModifyGuildUserAsync(guild.Id, affectedUserId, options => options.TimeOutUntil = default(DateTimeOffset));
				await interaction.SendResponseAsync(InteractionCallback.Message("Timeout removed!"));
				break;
		}
	}

	private static async ValueTask ProcessModalInteraction(ModalInteraction interaction)
	{
		Guild? guild = interaction.Guild;
		if (guild is null) return;

		InteractionDataContainer<ulong> interactionData = new(interaction.Data.CustomId);
		if (interactionData.Source != nameof(DuplicateMessageCleanerService)) return;

		string modalAction = interactionData.Type;
		ulong affectedUserId = interactionData.Extra;

		switch(modalAction)
		{
			case MODAL_BAN:
				RadioGroup? radioGroup = interaction.Data.Components //
					.OfType<Label>() //
					.Select(component => component.Component) //
					.OfType<RadioGroup>() //
					.FirstOrDefault(radioGroup => radioGroup.CustomId == MODAL_BAN_DELETE_RADIOS);
				if (radioGroup is null) return;

				int hours = int.Parse(radioGroup.SelectedValue ?? "0");
				if (hours == 0)
				{
					await _client.Rest.BanGuildUserAsync(guild.Id, affectedUserId);
					await interaction.SendResponseAsync(InteractionCallback.Message("Banned!"));
				}
				else
				{
					int seconds = hours * 60 * 60;
					await _client.Rest.BanGuildUserAsync(guild.Id, affectedUserId, seconds);
					await interaction.SendResponseAsync(InteractionCallback.Message($"Banned and messages from the previous {hours} hours were cleaned too!"));
				}
				break;
		}
	}

	private static async Task NotifyMods(Message prevMessage, Message thisMessage)
	{
		ulong? guildId = prevMessage.GuildId;
		if (guildId == null) return;

		if (_modChannels.TryGetValue(guildId.Value, out ulong channelId))
		{
			await _client.Rest.SendMessageAsync(channelId, new MessageProperties
			{
				MessageReference = MessageReferenceProperties.Forward(thisMessage.ChannelId, thisMessage.Id),
			});

			await _client.Rest.SendMessageAsync(channelId, new MessageProperties
			{
				Components =
				[
					new TextDisplayProperties($"{prevMessage.Author} sent this↑ message in {prevMessage.Channel} and {thisMessage.Channel}!"),
					new TextDisplayProperties($"The messages have been cleaned up, and the account has been given a timeout of {TIMEOUT_TIME_MINUTES} minutes."),
					new ActionRowProperties([
						new ButtonProperties(new InteractionDataContainer<ulong>(
								nameof(DuplicateMessageCleanerService),
								BUTTON_ACTION_BAN,
								prevMessage.Author.Id
							).ToString(),
							"Ban",
							EmojiProperties.Standard("🔨"),
							ButtonStyle.Danger),
						new ButtonProperties(new InteractionDataContainer<ulong>(
								nameof(DuplicateMessageCleanerService),
								BUTTON_ACTION_REMOVE_TIMEOUT,
								prevMessage.Author.Id
							).ToString(),
							"Remove timeout",
							EmojiProperties.Standard("🔊"),
							ButtonStyle.Success),
					]),
				],
				Flags = MessageFlags.IsComponentsV2,
			});
		}
		else
		{
			IReadOnlyList<IGuildChannel> channels = await _client.Rest.GetGuildChannelsAsync(guildId.Value);
			TextGuildChannel? channel = channels.OfType<TextGuildChannel>().FirstOrDefault(channel => channel.Name.Contains("general", StringComparison.InvariantCultureIgnoreCase));
			if (channel is not null)
				await channel.SendMessageAsync(new MessageProperties
				{
					Content = """
					          A double message (likely spam) was just cleaned up!

					          _A moderator should set up a moderation notification channel with `/set-dupe-notif-channel` so that the moderators can see information about this, the next time this happens._
					          """,
				});
		}
	}

	/// <summary>
	/// Transforms a Message into a String that can be compared for spam/duplicate detection.
	/// The special thing is that it also takes attachments into account!
	/// </summary>
	private static string GetContentFromMessage(Message message)
	{
		if (message.Attachments.Count == 0) return message.Content;

		StringBuilder result = new(message.Content);
		foreach(Attachment attachment in message.Attachments)
		{
			Uri uri = new(attachment.Url);
			if (result.Length != 0) result.AppendLine();
			result.Append(Path.GetFileName(uri.LocalPath));
		}
		return result.ToString();
	}

	private static async Task DeleteMessageIfExists(Message message)
	{
		try
		{
			await message.DeleteAsync();
		}
		catch(RestException restException) when(restException.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			//ignore
		}
	}

	private static async Task TimeoutUser(GuildUser guildUser)
	{
		try
		{
			await guildUser.TimeOutAsync(DateTimeOffset.Now.AddMinutes(TIMEOUT_TIME_MINUTES));
		}
		catch(RestException restException) when(restException.StatusCode == System.Net.HttpStatusCode.Forbidden)
		{
			//ignore
		}
	}

	private static void SaveModChannels()
	{
		Dictionary<ulong, ulong> dict = _modChannels.ToDictionary();
		using FileStream fs = File.Open(MOD_CHANNELS_FILE, FileMode.Create);
		JsonSerializer.Serialize(fs, dict, ToJson.Default.DictionaryUInt64UInt64);
	}
}
