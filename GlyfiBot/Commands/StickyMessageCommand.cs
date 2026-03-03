using JetBrains.Annotations;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Text.Json;

namespace GlyfiBot.Commands;

public class StickyMessageCommand : ApplicationCommandModule<SlashCommandContext>
{
	private const string MESSAGES_FILE = Program.STICKY_DIR + "/messages.json";
	private const string PREVIOUS_FILE = Program.STICKY_DIR + "/previous.json";

	/// Channel ID, Sticky Message Content
	private static Dictionary<ulong, string> _stickyMessages = null!;
	/// Channel ID, Previous Message ID
	private static Dictionary<ulong, ulong> _previousMessages = null!;

	private static GatewayClient _client = null!;
	private static ulong _botUserId;

	public static async Task Load(GatewayClient client)
	{
		Directory.CreateDirectory(Program.STICKY_DIR);
		if (File.Exists(MESSAGES_FILE))
		{
			string json = await File.ReadAllTextAsync(MESSAGES_FILE);
			_stickyMessages = JsonSerializer.Deserialize(json, ToJson.Default.DictionaryUInt64String)!;
		}
		else
		{
			_stickyMessages = new Dictionary<ulong, string>();
		}

		if (File.Exists(PREVIOUS_FILE))
		{
			string json = await File.ReadAllTextAsync(PREVIOUS_FILE);
			_previousMessages = JsonSerializer.Deserialize(json, ToJson.Default.DictionaryUInt64UInt64)!;
		}
		else
		{
			_previousMessages = new Dictionary<ulong, ulong>();
		}

		_client = client;
		_botUserId = (await _client.Rest.GetCurrentUserAsync()).Id;

		_client.MessageCreate += async message => await ProcessMessage(message);
	}

	private static async Task ProcessMessage(Message message)
	{
		if (message.Author.Id == _botUserId) return; // Do not reply after own messages

		Channel? channel = message.Channel;
		if (channel is null) return;

		if (_stickyMessages.TryGetValue(channel.Id, out string? stickyMessage))
		{
			await SendMessage(channel, stickyMessage);
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
		if (message is null || message.IsWhiteSpace())
		{
			await RemoveStickyRegistration(channel);
			await Context.SendEphemeralResponseAsync("Sticky Message disabled for this channel.");
		}
		else
		{
			await Context.SendEphemeralResponseAsync("Sticky Message enabled for this channel.");
			await AddStickyRegistration(channel, message);
		}
	}

	private static async Task AddStickyRegistration(Channel channel, string message)
	{
		_stickyMessages[channel.Id] = message;
		await SendMessage(channel, message);
		SaveStickyMessages();
	}

	private static async Task RemoveStickyRegistration(Channel channel)
	{
		_stickyMessages.Remove(channel.Id);
		_previousMessages.Remove(channel.Id);
		await DeletePreviousMessage(channel);
		SaveStickyMessages();
		SavePreviousMessages();
	}

	// TODO: Do not run for every single message that comes in (maybe at most once every five seconds or so)
	/// Delete previous message, send new message, and store for later deletion
	private static async Task SendMessage(Channel channel, string message)
	{
		await Task.WhenAll(
			DeletePreviousMessage(channel),
			SendAndOverwriteDict()
		);
		SavePreviousMessages();
		return;

		async Task SendAndOverwriteDict()
		{
			await Task.Yield();
			RestMessage sentMessage = await _client.Rest.SendMessageAsync(channel.Id, message);
			_previousMessages[channel.Id] = sentMessage.Id;
		}
	}

	private static async Task DeletePreviousMessage(Channel channel)
	{
		if (_previousMessages.TryGetValue(channel.Id, out ulong previousMessage))
		{
			await Task.Yield();
			await _client.Rest.DeleteMessageAsync(channel.Id, previousMessage);
		}
	}

	private static void SaveStickyMessages()
	{
		string json = JsonSerializer.Serialize(_stickyMessages, ToJson.Default.DictionaryUInt64String);
		File.WriteAllText(MESSAGES_FILE, json);
	}

	private static void SavePreviousMessages()
	{
		//TODO: Save on a timer, to prevent overload of IO spam (maybe 5 minutes or so)
		string json = JsonSerializer.Serialize(_previousMessages, ToJson.Default.DictionaryUInt64UInt64);
		File.WriteAllText(PREVIOUS_FILE, json);
	}
}
