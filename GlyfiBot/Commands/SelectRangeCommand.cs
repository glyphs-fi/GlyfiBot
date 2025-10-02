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

		StringBuilder sb = new();
		sb.Append($"Selected messages: {messages.Count}\n\n");

		uint caughtMessages = 0;
		foreach(DiscordMessage message in messages)
		{
			if (message.Attachments.Count == 0) continue;

			foreach(DiscordReaction reaction in message.Reactions)
			{
				if (reaction.Emoji != emoji) continue;

				await foreach(DiscordUser user in message.GetReactionsAsync(emoji))
				{
					if (user == message.Author)
					{
						caughtMessages++;
						foreach(DiscordAttachment attachment in message.Attachments)
						{
							sb.Append($"- {attachment.Url}\n");
						}
					}
				}
			}
		}

		sb.Append($"\nCaught messages: {caughtMessages}");
		string total = sb.ToString();
		Console.WriteLine(total);

		if (string.IsNullOrWhiteSpace(total))
		{
			await context.SendEphemeralResponse("No results found!");
			return;
		}

		await context.SendEphemeralResponse(total);
	}
}
