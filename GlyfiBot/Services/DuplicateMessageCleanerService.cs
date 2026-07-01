using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using System.Collections.Concurrent;
using System.Text;

namespace GlyfiBot.Services;

public static class DuplicateMessageCleanerService
{
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
	private const int MUTE_TIME_MINUTES = 60;

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
					// If they are the same, then we delete both messages and mute the user
					await Task.WhenAll(
						DeleteMessageIfExists(thisMessage),
						DeleteMessageIfExists(prevMessage),
						MuteUser(guildUser)
					);
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

	private static async Task MuteUser(GuildUser guildUser)
	{
		try
		{
			await guildUser.TimeOutAsync(DateTimeOffset.Now.AddMinutes(MUTE_TIME_MINUTES));
		}
		catch(RestException restException) when(restException.StatusCode == System.Net.HttpStatusCode.Forbidden)
		{
			//ignore
		}
	}

}
