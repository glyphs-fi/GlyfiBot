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
	public enum DownloadType
	{
		Raw,
		Flat,
	}

	private record AttachmentFile(string DownloadUrl, string FileName);

	[Command("select")]
	[Description("Select messages to look through for submissions")]
	[RequirePermissions([], [DiscordPermission.Administrator])]
	[UsedImplicitly]
	public static async ValueTask ExecuteAsync(SlashCommandContext context, ulong start, ulong end, DownloadType downloadType = DownloadType.Flat)
	{
		DiscordEmoji? emoji = SetTheEmojiCommand.TheEmoji;
		if (emoji is null)
		{
			await context.SendEphemeralResponseAsync("Emoji has not been set! Use `/set-emoji` to set the emoji first");
			return;
		}

		DiscordChannel channel = context.Channel;

		// If the order is wrong, swap them into the correct order
		if (start > end) (start, end) = (end, start);

		DiscordMessage? msgStart = await GetMessageAsync(context, start);
		if (msgStart is null) return;

		DiscordMessage? msgEnd = await GetMessageAsync(context, end);
		if (msgEnd is null) return;

		await context.DeferResponseAsync(true);

		List<DiscordMessage> messages = await GetMessagesBetweenAsync(channel, start, end);

		(Dictionary<DiscordUser, List<AttachmentFile>> submissions, uint submissionMessageCount) = await FilterSubmissionsFromMessagesAsync(messages, emoji);

		StringBuilder sb = new();
		sb.AppendLine($"Selected messages: {messages.Count}");
		sb.AppendLine($"Found submission messages: {submissionMessageCount}");

		long submissionsCount = submissions.Aggregate(0, (current, keyValuePair) => current + keyValuePair.Value.Count);
		sb.AppendLine($"Found total submissions: {submissionsCount}");

		string? submissionArchivePath = null;
		if (submissionsCount > 0)
		{
			sb.AppendLine();
			string directoryToArchive = await DownloadAttachmentsAsync(context.Interaction, submissions, sb, downloadType);
			submissionArchivePath = Path.Join(Path.GetDirectoryName(directoryToArchive), $"{context.Interaction.Id}_{Path.GetFileName(directoryToArchive)}.zip");
			bool includeBaseDirectory = downloadType switch
			{
				DownloadType.Raw => true,
				DownloadType.Flat => false,
				_ => throw new ArgumentOutOfRangeException(nameof(downloadType), downloadType, null),
			};
			ZipFile.CreateFromDirectory(directoryToArchive, submissionArchivePath, CompressionLevel.SmallestSize, includeBaseDirectory);
		}

		string total = sb.ToString();
		Console.WriteLine(total);

		try
		{
			await context.SendEphemeralResponseAsync(total, submissionArchivePath);
		}
		catch(RequestSizeException)
		{
			await context.SendEphemeralResponseAsync(total + "# Archive too big, could not upload...");
		}
	}

	private static async Task<string> DownloadAttachmentsAsync(DiscordInteraction interaction, Dictionary<DiscordUser, List<AttachmentFile>> submissions, StringBuilder sb, DownloadType downloadType)
	{
		string channelPath = Path.Join(Program.SELECTIONS_DIR, interaction.ChannelId.ToString());
		Directory.CreateDirectory(channelPath);

		string submissionPath = Path.Join(channelPath, interaction.Id.ToString());
		Directory.CreateDirectory(submissionPath);

		string rawPath = Path.Join(submissionPath, "raw");
		Directory.CreateDirectory(rawPath);

		string flatPath = Path.Join(submissionPath, "flat");
		if (downloadType == DownloadType.Flat)
		{
			Directory.CreateDirectory(flatPath);
		}

		using HttpClient client = new();
		foreach((DiscordUser author, List<AttachmentFile> attachmentFiles) in submissions)
		{
			sb.AppendLine($"- {author.Mention}");
			string submissionUserPath = Path.Join(rawPath, author.Id.ToString());
			Directory.CreateDirectory(submissionUserPath);
			uint antiDuplicateCounter = 0;
			for(int i = 0; i < attachmentFiles.Count; i++)
			{
				AttachmentFile attachmentFile = attachmentFiles[i];
				sb.AppendLine($"  - {attachmentFile.DownloadUrl}");
				string path = CreateDownloadFilePath();
				if (File.Exists(path))
				{
					antiDuplicateCounter++;
					path = CreateDownloadFilePath();
				}

				{
					await using Stream networkStream = await client.GetStreamAsync(attachmentFile.DownloadUrl);
					await using FileStream fileStream = new(path, FileMode.CreateNew);
					await networkStream.CopyToAsync(fileStream);
				}

				if (downloadType == DownloadType.Flat)
				{
					string flatFilename = author.Username + (attachmentFiles.Count > 1 ? $"_{i}" : "") + Path.GetExtension(path);
					File.Copy(path, Path.Join(flatPath, flatFilename));
				}
				continue;

				string CreateDownloadFilePath() => antiDuplicateCounter == 0
					? Path.Join(submissionUserPath, attachmentFile.FileName)
					: Path.Join(submissionUserPath, $"{Path.GetFileNameWithoutExtension(attachmentFile.FileName)} ({antiDuplicateCounter}){Path.GetExtension(attachmentFile.FileName)}");
			}
		}

		return downloadType switch
		{
			DownloadType.Raw => rawPath,
			DownloadType.Flat => flatPath,
			_ => throw new ArgumentOutOfRangeException(nameof(downloadType), downloadType, null),
		};
	}

	private static async Task<(Dictionary<DiscordUser, List<AttachmentFile>> submissions, uint submissionMessageCount)> FilterSubmissionsFromMessagesAsync(List<DiscordMessage> messages, DiscordEmoji emoji)
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
