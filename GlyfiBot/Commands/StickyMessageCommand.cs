using JetBrains.Annotations;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Collections.Concurrent;
using System.Text.Json;

namespace GlyfiBot.Commands;

public class StickyMessageCommand : ApplicationCommandModule<SlashCommandContext>
{
	private const string MESSAGES_FILE = $"{Program.SETTINGS_DIR}/stickies.json";

	private static ConcurrentDictionary<ulong, WatchedChannel> _stickyMessages = null!;

	private static GatewayClient _client = null!;

	/// The delay for between sending the sticky message.<br/>
	/// Used in <see cref="WatchedChannel.SendMessageDelayed"/>
	private static readonly TimeSpan _delay = TimeSpan.FromSeconds(1);

	public static async Task Load(GatewayClient client)
	{
		_client = client;

		if (File.Exists(MESSAGES_FILE))
		{
			await using FileStream fs = File.OpenRead(MESSAGES_FILE);
			Dictionary<ulong, string> dict = (await JsonSerializer.DeserializeAsync(fs, ToJson.Default.DictionaryUInt64String))!;
			_stickyMessages = new ConcurrentDictionary<ulong, WatchedChannel>(dict.Select(WatchedChannel.FromJson));
			await Task.WhenAll(_stickyMessages.Values.Select(channel => channel.GetPreviousMessageId()));
		}
		else
		{
			_stickyMessages = new ConcurrentDictionary<ulong, WatchedChannel>();
		}

		_client.MessageCreate += ProcessMessage;
	}

	private static async ValueTask ProcessMessage(Message message)
	{
		if (message.Author.Id == Program.BotUser.Id) return; // Do not reply after own messages

		ulong channelId = message.ChannelId;
		if (_stickyMessages.TryGetValue(channelId, out WatchedChannel? watchedChannel))
		{
			await watchedChannel.SendMessageDelayed();
		}
	}

	[SlashCommand("sticky",
		"Sets a sticky message for this channel",
		DefaultGuildPermissions = Permissions.Administrator)]
	[UsedImplicitly]
	public async Task ExecuteAsync(
		[SlashCommandParameter(Description = "Set the message, omit to disable")]
		string? message = null
	)
	{
		Channel channel = Context.Channel;
		if (message.IsNullOrWhiteSpace())
		{
			await RemoveStickyRegistration(channel);
			await Context.SendEphemeralResponseAsync("Sticky Message disabled for this channel.");
		}
		else
		{
			await AddStickyRegistration(channel, message);
			await Context.SendEphemeralResponseAsync("Sticky Message enabled for this channel.");
		}
		await SaveStickyMessages();
	}

	private static async Task AddStickyRegistration(Channel channel, string message)
	{
		if (_stickyMessages.TryGetValue(channel.Id, out WatchedChannel? watchedChannel))
		{
			watchedChannel.ChangeMessage(message);
		}
		else
		{
			watchedChannel = new WatchedChannel(channel.Id, message);
			_stickyMessages.TryAdd(channel.Id, watchedChannel);
		}
		await watchedChannel.SendMessageInstantly();
	}

	private static async Task RemoveStickyRegistration(Channel channel)
	{
		if (_stickyMessages.TryGetValue(channel.Id, out WatchedChannel? watchedChannel))
		{
			await watchedChannel.DeletePreviousMessage();
			_stickyMessages.TryRemove(channel.Id, out _);
		}
	}

	private class WatchedChannel(ulong channelId, string message)
	{
		private string _message = message;
		private ulong? _previousMessageId;

		private bool _busySending = false;

		public async Task GetPreviousMessageId()
		{
			// Loop through most recent messages to find the previously sent message
			bool isCaughtUp = true;
			IAsyncEnumerable<RestMessage> asyncEnumerable = _client.Rest.GetMessagesAsync(channelId);
			await foreach(RestMessage message in asyncEnumerable)
			{
				if (message.Author.Id == Program.BotUser.Id && message.Content == _message)
				{
					_previousMessageId = message.Id;
					break;
				}
				isCaughtUp = false;
			}

			// If the previously sent message is not the most recent message in the channel, ensure that it now is
			if (!isCaughtUp)
			{
				await SendMessageInstantly();
			}
		}

		public void ChangeMessage(string newMessage)
		{
			_message = newMessage;
		}

		/// Do not run for every single message that comes in, but only every <see cref="_delay"/>
		public async Task SendMessageDelayed()
		{
			// If another thread/task is already busy sending, don't send for this one
			if (Interlocked.Exchange(ref _busySending, true)) return;
			try
			{
				await Task.Delay(_delay);
				await SendMessageAfterDelay();
			}
			finally
			{
				Interlocked.Exchange(ref _busySending, false);
			}
		}

		/// Checks if the last message is still the last message before sending, in case the new messages were deleted
		private async Task SendMessageAfterDelay()
		{
			if (_previousMessageId is not null)
			{
				IAsyncEnumerable<RestMessage> asyncEnumerable = _client.Rest.GetMessagesAsync(
					channelId,
					new PaginationProperties<ulong>
					{
						Direction = PaginationDirection.After,
						From = _previousMessageId,
					});
				List<RestMessage> messagesFromThenTillNow = await asyncEnumerable.ToListAsync();
				if (messagesFromThenTillNow.Count == 0)
				{
					// Don't do anything; the new messages have already been deleted
					return;
				}
			}
			await SendMessageInstantly();
		}

		/// Delete previous message, send new message, and store for later deletion
		public async Task SendMessageInstantly()
		{
			await Task.WhenAll(
				DeletePreviousMessage(),
				SendAndOverwriteDict()
			);
			return;

			async Task SendAndOverwriteDict()
			{
				await Task.Yield();
				RestMessage sentMessage = await _client.Rest.SendMessageAsync(channelId, _message);
				_previousMessageId = sentMessage.Id;
			}
		}

		public async Task DeletePreviousMessage()
		{
			if (_previousMessageId.HasValue)
			{
				await Task.Yield();
				await _client.Rest.DeleteMessageAsync(channelId, _previousMessageId.Value);
			}
		}

		public static KeyValuePair<ulong, WatchedChannel> FromJson(KeyValuePair<ulong, string> pair)
		{
			return new KeyValuePair<ulong, WatchedChannel>(pair.Key, new WatchedChannel(pair.Key, pair.Value));
		}

		public KeyValuePair<ulong, string> ToJson()
		{
			return new KeyValuePair<ulong, string>(channelId, _message);
		}
	}

	private static async Task SaveStickyMessages()
	{
		Dictionary<ulong, string> dict = _stickyMessages.Select(pair => pair.Value.ToJson()).ToDictionary();
		await using FileStream fs = File.Open(MESSAGES_FILE, FileMode.Create);
		await JsonSerializer.SerializeAsync(fs, dict, ToJson.Default.DictionaryUInt64String);
	}
}
