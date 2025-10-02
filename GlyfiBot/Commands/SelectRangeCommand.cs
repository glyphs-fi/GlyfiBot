using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using JetBrains.Annotations;
using System.Text;
using static GlyfiBot.Utils;

namespace GlyfiBot.Commands;

public class SelectRangeCommand
{
	[Command("select")]
	[UsedImplicitly]
	public static async ValueTask ExecuteAsync(SlashCommandContext context, ulong start, ulong end)
	{
		DiscordEmoji? emoji = Program.TheEmoji;
		if (emoji is null)
		{
			await context.SendEphemeralResponse("Emoji has not been set! Use `/set` to set the emoji first");
			return;
		}

		DiscordChannel channel = context.Channel;

		// If the order is wrong, swap them into the correct order
		if (start > end) (start, end) = (end, start);

		DiscordMessage? msgStart = await GetMessage(context, start);
		if (msgStart is null) return;

		DiscordMessage? msgEnd = await GetMessage(context, end);
		if (msgEnd is null) return;

		await context.DeferResponseAsync(true);

		List<DiscordMessage> messages = await GetMessagesBetween(channel, start, end);

		uint submissionMessageCount = 0;
		Dictionary<DiscordUser, List<string>> submissions = [];
		foreach(DiscordMessage message in messages)
		{
			if (message.Attachments.Count == 0) continue;

			foreach(DiscordReaction reaction in message.Reactions)
			{
				if (reaction.Emoji != emoji) continue;

				await foreach(DiscordUser user in message.GetReactionsAsync(emoji))
				{
					if (user != message.Author) continue;

					submissionMessageCount++;
					foreach(DiscordAttachment attachment in message.Attachments)
					{
						if (attachment.Url is null) continue;

						if (submissions.TryGetValue(message.Author, out List<string>? urls))
						{
							urls.Add(attachment.Url);
						}
						else
						{
							submissions.Add(message.Author, [attachment.Url]);
						}
					}
				}
			}
		}

		StringBuilder sb = new();
		sb.Append($"Selected messages: {messages.Count}\n");
		sb.Append($"Found submission messages: {submissionMessageCount}\n");

		long submissionsCount = submissions.Aggregate(0, (current, keyValuePair) => current + keyValuePair.Value.Count);
		sb.Append($"Found total submissions: {submissionsCount}\n");

		sb.Append('\n');
		foreach((DiscordUser author, List<string> urls) in submissions)
		{
			sb.Append($"- {author.Mention}\n");
			foreach(string url in urls)
			{
				sb.Append($"\t- {url}\n");
			}
		}

		string total = sb.ToString();
		Console.WriteLine(total);

		await context.SendEphemeralResponse(total);
	}
}
