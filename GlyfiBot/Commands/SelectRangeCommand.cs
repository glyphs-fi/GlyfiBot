using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using JetBrains.Annotations;
using System.ComponentModel;
using System.IO.Compression;
using System.Text;
using static GlyfiBot.Utils;

namespace GlyfiBot.Commands;

public class SelectRangeCommand
{
	private record AttachmentFile(string DownloadUrl, string FileName);

	[Command("select")]
	[Description("Select messages to look through for submissions")]
	[RequirePermissions([], [DiscordPermission.Administrator])]
	[UsedImplicitly]
	public static async ValueTask ExecuteAsync(SlashCommandContext context, ulong start, ulong end)
	{
		DiscordEmoji? emoji = SetTheEmojiCommand.TheEmoji;
		if (emoji is null)
		{
			await context.SendEphemeralResponse("Emoji has not been set! Use `/set-emoji` to set the emoji first");
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

		(Dictionary<DiscordUser, List<AttachmentFile>> submissions, uint submissionMessageCount) = await FilterSubmissionsFromMessages(messages, emoji);

		StringBuilder sb = new();
		sb.AppendLine($"Selected messages: {messages.Count}");
		sb.AppendLine($"Found submission messages: {submissionMessageCount}");

		long submissionsCount = submissions.Aggregate(0, (current, keyValuePair) => current + keyValuePair.Value.Count);
		sb.AppendLine($"Found total submissions: {submissionsCount}");

		string? submissionArchivePath = null;
		if (submissionsCount > 0)
		{
			sb.AppendLine();
			string submissionPath = await DownloadAttachments(context.Interaction, submissions, sb);
			submissionArchivePath = Path.Join(Path.GetDirectoryName(submissionPath), Path.GetFileName(submissionPath) + ".zip");
			ZipFile.CreateFromDirectory(submissionPath, submissionArchivePath, CompressionLevel.SmallestSize, includeBaseDirectory: true);
		}

		string total = sb.ToString();
		Console.WriteLine(total);

		try
		{
			await context.SendEphemeralResponse(total, submissionArchivePath);
		}
		catch(RequestSizeException)
		{
			await context.SendEphemeralResponse(total + "# Archive too big, could not upload...");
		}
	}

	private static async Task<string> DownloadAttachments(DiscordInteraction interaction, Dictionary<DiscordUser, List<AttachmentFile>> submissions, StringBuilder sb)
	{
		string channelPath = Path.Join(Program.SELECTIONS_DIR, interaction.ChannelId.ToString());
		Directory.CreateDirectory(channelPath);

		string submissionPath = Path.Join(channelPath, interaction.Id.ToString());
		Directory.CreateDirectory(submissionPath);

		using HttpClient client = new();
		foreach((DiscordUser author, List<AttachmentFile> attachmentFiles) in submissions)
		{
			sb.AppendLine($"- {author.Mention}");
			string submissionUserPath = Path.Join(submissionPath, author.Id.ToString());
			Directory.CreateDirectory(submissionUserPath);
			uint antiDuplicateCounter = 0;
			foreach(AttachmentFile attachmentFile in attachmentFiles)
			{
				sb.AppendLine($"  - {attachmentFile.DownloadUrl}");
				string path = CreateDownloadFilePath();
				if (File.Exists(path))
				{
					antiDuplicateCounter++;
					path = CreateDownloadFilePath();
				}

				await using Stream networkStream = await client.GetStreamAsync(attachmentFile.DownloadUrl);
				await using FileStream fileStream = new(path, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
				continue;

				string CreateDownloadFilePath() => antiDuplicateCounter == 0
					? Path.Join(submissionUserPath, attachmentFile.FileName)
					: Path.Join(submissionUserPath, $"{Path.GetFileNameWithoutExtension(attachmentFile.FileName)} ({antiDuplicateCounter}){Path.GetExtension(attachmentFile.FileName)}");
			}
		}

		return submissionPath;
	}

	private static async Task<(Dictionary<DiscordUser, List<AttachmentFile>> submissions, uint submissionMessageCount)> FilterSubmissionsFromMessages(List<DiscordMessage> messages, DiscordEmoji emoji)
	{
		Dictionary<DiscordUser, List<AttachmentFile>> submissions = [];
		uint submissionMessageCount = 0;

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

						AttachmentFile attachmentFile = new(attachment.Url, attachment.FileName ?? attachment.Id.ToString());
						if (submissions.TryGetValue(message.Author, out List<AttachmentFile>? attachmentFiles))
						{
							attachmentFiles.Add(attachmentFile);
						}
						else
						{
							submissions.Add(message.Author, [attachmentFile]);
						}
					}
				}
			}
		}

		return (submissions, submissionMessageCount);
	}
}
