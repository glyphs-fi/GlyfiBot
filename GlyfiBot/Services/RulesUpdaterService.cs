using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using System.Text.Json;
using static GlyfiBot.Utils;

namespace GlyfiBot.Services;

public static class RulesUpdaterService
{
	/// How long between update checks
	private static readonly TimeSpan _timeSpan = TimeSpan.FromHours(1);

	private const string RULES_CHANNEL_FILE = $"{Program.SETTINGS_DIR}/rules_channel.txt";

	private const string RULES_REPO_NAME = "Rules";

	private static ulong? _channelId;

	private static GatewayClient _client = null!;

	public static async Task RunAsync(GatewayClient client)
	{
		_client = client;

		LoadRulesChannel();

		await UpdateRules();

		using PeriodicTimer timer = new(_timeSpan);

		while(await timer.WaitForNextTickAsync())
		{
			try
			{
				await UpdateRules();
			}
			catch(Exception e)
			{
				Console.WriteLine(e);
				await Task.Delay(TimeSpan.FromSeconds(10)); // Some delay to prevent an exception from being thrown repeatedly
			}
		}
	}


	public static async Task SetChannelId(ulong channelId)
	{
		_channelId = channelId;
		SaveRulesChannel();
		await UpdateRules(force: true);
	}

	public static async Task ClearChannelId()
	{
		if (_channelId is not null)
		{
			await ClearOldRules(_channelId.Value);
		}

		_channelId = null;
		SaveRulesChannel();
	}

	private static void LoadRulesChannel()
	{
		if (File.Exists(RULES_CHANNEL_FILE))
		{
			_channelId = ulong.Parse(File.ReadAllText(RULES_CHANNEL_FILE));
		}
	}

	private static void SaveRulesChannel()
	{
		if (_channelId is null)
		{
			File.Delete(RULES_CHANNEL_FILE);
		}
		else
		{
			File.WriteAllText(RULES_CHANNEL_FILE, _channelId.Value.ToString());
		}
	}

	private static async Task UpdateRules(bool force = false)
	{
		if (_channelId is null) return;
		ulong channelId = _channelId.Value;

		(string allRulesDir, bool didDownload) = await DownloadRepo(RULES_REPO_NAME, Program.RULES_DIR);
		// if (!didDownload && !force) return; // Did not download, so there was nothing new, so we don't need to update

		string discordRulesDir = Path.Join(allRulesDir, "Discord");
		if (!Directory.Exists(discordRulesDir)) throw new DirectoryNotFoundException("Could not find Discord rules folder!");

		string[] files = Directory.GetFiles(discordRulesDir).Where(file =>
		{
			string fileName = Path.GetFileName(file);
			if (fileName.StartsWith('.')) return false;
			if (string.Equals(fileName, "README.md", StringComparison.InvariantCultureIgnoreCase)) return false;
			return true;
		}).ToArray();
		files.Sort();

		List<RestMessage> messages = await GetMessages(channelId);
		if (messages.Count == files.Length)
		{
			await EditRuleMessages(messages, files);
		}
		else
		{
			await ClearAndReSendRuleMessages(channelId, files);
		}
	}

	private static async Task EditRuleMessages(List<RestMessage> messages, string[] files)
	{
		for(int i = 0; i < messages.Count; i++)
		{
			RestMessage message = messages[i];
			string file = files[i];

			MessageProperties messageProperties;
			try
			{
				messageProperties = await GetMessagePropertiesForFile(file);
			}
			catch(JsonException e)
			{
				await message.Edit(GetMessagePropertiesForError(e, file));
				continue;
			}

			// If there is content and the content is the same, then we don't edit, cause there's no need to
			if (!messageProperties.Content.IsNullOrWhiteSpace() && messageProperties.Content.Trim() == message.Content.Trim()) continue;
			// I've decided to always edit the images, cause I'm not diffing those.

			try
			{
				await message.Edit(messageProperties);
			}
			catch(RestException e)
			{
				await message.Edit(GetMessagePropertiesForError(e, file));
			}
		}
	}

	private static async Task ClearAndReSendRuleMessages(ulong channelId, string[] files)
	{
		await ClearOldRules(channelId);

		foreach(string file in files)
		{
			try
			{
				await _client.Rest.SendMessageAsync(channelId, await GetMessagePropertiesForFile(file));
			}
			catch(RestException e)
			{
				await _client.Rest.SendMessageAsync(channelId, GetMessagePropertiesForError(e, file));
			}
			catch(JsonException e)
			{
				await _client.Rest.SendMessageAsync(channelId, GetMessagePropertiesForError(e, file));
			}
		}
	}

	private static async Task ClearOldRules(ulong channelId)
	{
		List<RestMessage> messages = await GetMessages(channelId);
		await _client.Rest.DeleteMessagesAsync(channelId, messages.Select(message => message.Id));
	}

	private static async Task<List<RestMessage>> GetMessages(ulong channelId)
	{
		IAsyncEnumerable<RestMessage> asyncEnumerable = _client.Rest.GetMessagesAsync(channelId, new PaginationProperties<ulong> {Direction = PaginationDirection.Before});
		List<RestMessage> messages = await asyncEnumerable.Where(message => message.Author.Id == Program.BotUser.Id).ToListAsync();
		messages.Sort((messageA, messageB) => messageA.Id.CompareTo(messageB.Id));
		return messages;
	}

	private static async Task<MessageProperties> GetMessagePropertiesForFile(string file)
	{
		string ext = Path.GetExtension(file).ToLowerInvariant();
		return ext switch
		{
			".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".webm" or ".mp4" => new MessageProperties
			{
				Attachments = [new AttachmentProperties(Path.GetFileName(file), new FileStream(file, FileMode.Open))],
			},
			".md" or ".txt" => new MessageProperties
			{
				Content = await File.ReadAllTextAsync(file),
			},
			".json" => JsonSerializer.Deserialize(await File.ReadAllTextAsync(file), ToJson.Default.MessageProperties)!,
			_ => throw new Exception($"Cannot handle file type {ext} from {file}"),
		};
	}

	private static MessageProperties GetMessagePropertiesForError(RestException restException, string file)
	{
		string error = JsonPrettyPrint(restException.Error!.ToString()).Replace("\\u0022", "'").Replace("\\u0027", "'");
		return new MessageProperties
		{
			Embeds =
			[
				new EmbedProperties
				{
					Title = $"Failed to send message: {restException.Message}",
					Description = $"```json\n{error}\n```",
					Footer = new EmbedFooterProperties {Text = file},
					Color = new Color(255, 0, 0),
				},
			],
		};
	}

	private static MessageProperties GetMessagePropertiesForError(JsonException jsonException, string file)
	{
		return new MessageProperties
		{
			Embeds =
			[
				new EmbedProperties
				{
					Title = $"Failed to send message: {jsonException.Message}",
					Description = $"```\n{jsonException.StackTrace}\n```",
					Footer = new EmbedFooterProperties {Text = file},
					Color = new Color(255, 0, 0),
				},
			],
		};
	}
}
