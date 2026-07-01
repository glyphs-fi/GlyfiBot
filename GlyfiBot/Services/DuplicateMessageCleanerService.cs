using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using System.Collections.Concurrent;
using System.Text;

namespace GlyfiBot.Services;

public static class DuplicateMessageCleanerService
{
	private static readonly ConcurrentDictionary<ulong, Message> _userMessages = new();

	private static GatewayClient _client = null!;
	private static ulong _botUserId;

	public static async Task RunAsync(GatewayClient client)
	{
		_client = client;
		_botUserId = (await _client.Rest.GetCurrentUserAsync()).Id;

		_client.MessageCreate += async message => await ProcessMessage(message);
	}

	private static async Task ProcessMessage(Message thisMessage)
	{
		ulong author = thisMessage.Author.Id;

		if (author == _botUserId) return; // Do not check own messages

		string thisContent = GetContentFromMessage(thisMessage);

		if (_userMessages.TryGetValue(author, out Message? prevMessage))
		{
			//TODO: Add time-between check
			string prevContent = GetContentFromMessage(prevMessage);
			if (thisContent == prevContent)
			{
				await Task.WhenAll(DeleteMessageIfExists(thisMessage), DeleteMessageIfExists(prevMessage));
				if (thisMessage.Author is GuildUser guildUser)
				{
					await guildUser.TimeOutAsync(DateTimeOffset.Now.AddHours(1)); //TODO: Add try/catch for permissions, in case attempted timeout of a higher ranked user. Perhaps earlier, to prevent mods from being hit by this mechanism?
				}
			}
			_userMessages.TryUpdate(author, thisMessage, prevMessage);
		}
		else
		{
			_userMessages.TryAdd(author, thisMessage);
		}
	}

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
}
