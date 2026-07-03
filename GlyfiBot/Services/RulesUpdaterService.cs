using NetCord.Gateway;
using NetCord.Rest;
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
		if (!didDownload && !force) return; // Did not download, so there was nothing new, so we don't need to update

		string discordRulesDir = Path.Join(allRulesDir, "Discord");
		if (!Directory.Exists(discordRulesDir)) throw new DirectoryNotFoundException("Could not find Discord rules folder!");

		string[] files = Directory.GetFiles(discordRulesDir);
		files.Sort();

		await ClearOldRules(channelId);

		foreach(string file in files)
		{
			string ext = Path.GetExtension(file).ToLowerInvariant();
			switch(ext)
			{
				case ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".webm" or ".mp4":
					await _client.Rest.SendMessageAsync(channelId, new MessageProperties
					{
						Attachments = [new AttachmentProperties(Path.GetFileName(file), new FileStream(file, FileMode.Open))],
					});
					break;
				case ".md" or ".txt":
					string fileContents = await File.ReadAllTextAsync(file);
					await _client.Rest.SendMessageAsync(channelId, new MessageProperties
					{
						Content = fileContents,
					});
					break;
				default:
					throw new Exception($"Cannot handle file type {ext} from {file}");
			}

		}
	}

	private static async Task ClearOldRules(ulong channelId)
	{
		IAsyncEnumerable<RestMessage> asyncEnumerable = _client.Rest.GetMessagesAsync(channelId, new PaginationProperties<ulong> {Direction = PaginationDirection.Before});
		List<RestMessage> messages = await asyncEnumerable.ToListAsync();
		await _client.Rest.DeleteMessagesAsync(channelId, messages.Where(message => message.Author.Id == Program.BotUser.Id).Select(message => message.Id));
	}
}
