using JetBrains.Annotations;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.IO.Compression;
using System.Text;
using static GlyfiBot.Utils;

namespace GlyfiBot.Commands;

public enum DownloadType
{
	Raw,
	Flat,
}
public class SelectRangeCommand : ApplicationCommandModule<SlashCommandContext>
{
	[SlashCommand("select",
		"Select messages to look through for submissions",
		DefaultGuildPermissions = Permissions.Administrator)]
	[UsedImplicitly]
	public async Task ExecuteAsync(string start, string? end = null, DownloadType downloadType = DownloadType.Flat)
	{
		ReactionEmojiProperties? emoji = SetTheEmojiCommand.GetSubmissionEmoji(Context.Channel);
		if (emoji is null)
		{
			throw new SimpleCommandFailException("Emoji has not been set for this channel! Use `/set-emoji submission` to set the emoji first");
		}

		if (!ulong.TryParse(start, null, out ulong idStart))
		{
			throw new SimpleCommandFailException("`start` needs to be a number: the Message ID");
		}

		await VerifyThatMessageIsInChannel(Context, idStart);

		ulong? idEnd = null;
		if (end is not null)
		{
			if (!ulong.TryParse(end, null, out ulong idEndLocal))
			{
				throw new SimpleCommandFailException("`end` needs to be a number: the Message ID");
			}

			// If the order is wrong, swap them into the correct order
			if (idStart > idEndLocal) (idStart, idEndLocal) = (idEndLocal, idStart);

			await VerifyThatMessageIsInChannel(Context, idEndLocal);

			idEnd = idEndLocal;
		}

		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

		List<RestMessage> messages = await GetMessagesBetweenAsync(Context, idStart, idEnd);

		(Dictionary<User, List<Sub>> submissions, uint submissionMessageCount) = await FilterSubmissionsFromMessagesAsync(Context.Guild, Context.Channel, messages, emoji);

		StringBuilder sbStats = new();
		sbStats.AppendLine($"Selected messages: {messages.Count}");
		sbStats.AppendLine($"Found submission messages: {submissionMessageCount}");

		long submissionsCount = submissions.Aggregate(0, (current, keyValuePair) => current + keyValuePair.Value.Count);
		sbStats.AppendLine($"Found total submissions: {submissionsCount}");

		string? submissionArchivePath = null;
		StringBuilder sbList = new();
		if (submissionsCount > 0)
		{
			string directoryToArchive = await DownloadAttachmentsAsync(Context.Interaction, submissions, sbList, downloadType);
			submissionArchivePath = Path.Join(Path.GetDirectoryName(directoryToArchive), $"{Context.Interaction.Id}_{Path.GetFileName(directoryToArchive)}.zip");
			bool includeBaseDirectory = downloadType switch
			{
				DownloadType.Raw => true,
				DownloadType.Flat => false,
				_ => throw new ArgumentOutOfRangeException(nameof(downloadType), downloadType, null),
			};
			await ZipFile.CreateFromDirectoryAsync(directoryToArchive, submissionArchivePath, CompressionLevel.SmallestSize, includeBaseDirectory);
		}

		string stats = sbStats.ToString();
		string fileList = sbList.ToString();
		try
		{
			await ModifyResponseAsync(msg =>
			{
				msg.Flags = MessageFlags.Ephemeral | MessageFlags.SuppressEmbeds;

				List<AttachmentProperties> attachments = [];

				if (stats.Length + fileList.Length >= 1950) //a bit of a margin to the 2000 max
				{
					msg.Content = stats;
					attachments.Add(new AttachmentProperties("filelist.txt", new MemoryStream(Encoding.UTF8.GetBytes(fileList))));
				}
				else
				{
					msg.Content = stats + "\n" + fileList;
				}

				if (submissionArchivePath is not null)
				{
					attachments.Add(new AttachmentProperties(Path.GetFileName(submissionArchivePath), new FileStream(submissionArchivePath, FileMode.Open)));
				}

				msg.Attachments = attachments;
			});
		}
		catch(RestException e)
		{
			if (e.HasInternalError("BASE_TYPE_MAX_LENGTH"))
			{
				await Context.ModifyEphemeralResponseAsync("Message was too long to fit. Please file a bug report and paste the _exact_ command you used into it: <https://github.com/glyphs-fi/GlyfiBot/issues/new>");
			}
			else if (e.Error is {Code: 40005}) //Request entity too large
			{
				await Context.ModifyEphemeralResponseAsync(
					stats + "\n" +
					"Archive ended up being too big for Discord...\n" +
					"I'm afraid you'll have to collect the submissions manually until [#2](<https://github.com/glyphs-fi/GlyfiBot/issues/2>) and [#3](<https://github.com/glyphs-fi/GlyfiBot/issues/3>) are implemented...");
			}
			else
			{
				Console.WriteLine(e);
			}
		}
	}

	private static async Task<string> DownloadAttachmentsAsync(SlashCommandInteraction interaction, Dictionary<User, List<Sub>> submissions, StringBuilder sb, DownloadType downloadType)
	{
		string channelPath = Path.Join(Program.SELECTIONS_DIR, interaction.Channel.Id.ToString());
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

		foreach((User author, List<Sub> attachmentFiles) in submissions)
		{
			sb.AppendLine($"- {author.ToString()}");
			string submissionUserPath = Path.Join(rawPath, author.Id.ToString());
			Directory.CreateDirectory(submissionUserPath);
			uint antiDuplicateCounter = 0;
			for(int i = 0; i < attachmentFiles.Count; i++)
			{
				Sub attachmentFile = attachmentFiles[i];
				sb.AppendLine($"  - {attachmentFile.Url}");
				string path = CreateDownloadFilePath();
				if (File.Exists(path))
				{
					antiDuplicateCounter++;
					path = CreateDownloadFilePath();
				}

				{
					await using Stream networkStream = await Program.HttpClient.GetStreamAsync(attachmentFile.Url);
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

	public class Sub
	{
		public string FileName { get; }
		public Uri Url { get; }

		public Sub(Attachment fromAttachment)
		{
			FileName = fromAttachment.FileName;
			Url = new Uri(fromAttachment.Url);
		}

		public Sub(Embed fromEmbed)
		{
			// We use the Discord ProxyUrl to try and keep things a little more private and secure. We don't want to connect to suspicious servers if we can avoid it.
			Url = new Uri(fromEmbed.Thumbnail?.ProxyUrl ?? throw new InvalidOperationException("Embed Thumbnail ProxyUrl is missing"));
			FileName = fromEmbed.Title ?? Path.GetFileName(Url.AbsolutePath);
		}
	}

	public static async Task<(Dictionary<User, List<Sub>> submissions, uint submissionMessageCount)> FilterSubmissionsFromMessagesAsync(
		Guild? guild,
		TextChannel channel,
		List<RestMessage> messages,
		ReactionEmojiProperties emoji
	)
	{
		Dictionary<User, List<Sub>> submissions = [];
		uint submissionMessageCount = 0;

		ReactionEmojiProperties? disqualificationEmoji = SetTheEmojiCommand.GetDisqualificationEmoji(channel);

		foreach(RestMessage message in messages)
		{
			// Does this message even contain anything that could be a submission?
			if (message.Attachments.Count == 0 && message.Embeds.Count == 0) continue;

			// Has this message been marked as a submission, through the reaction emoji?
			if (!message.HasBeenReactedToWith(emoji)) continue;

			// Is the message disqualified, through the disqualification reaction emoji?
			if (await IsMessageDisqualified(guild, message, disqualificationEmoji)) continue;

			// Loop over all users who reacted to this message with the submission emoji...
			await foreach(User user in message.GetReactionsAsync(emoji))
			{
				// ...to find the user who submitted the message, because only they themselves are allowed to mark their own message as a submission
				if (user != message.Author) continue;

				submissionMessageCount++;

				List<Sub> allPotentialSubs =
				[
					// Check attachments (direct uploads)
					.. message.Attachments.Select(attachment => new Sub(attachment)),
					// Check embeds (includes content that might be linked, instead of uploaded)
					.. message.Embeds.Where(embed => embed.Type == EmbedType.Image).Select(embed => new Sub(embed)),
				];
				if (allPotentialSubs.Count == 0) continue;

				// Ensure that the `submissions` dictionary has an entry for this author...
				if (!submissions.TryGetValue(message.Author, out List<Sub>? authorSubs))
				{
					submissions[message.Author] = authorSubs = [];
				}

				// ...and then add all the submissions to the dictionary
				authorSubs.AddRange(allPotentialSubs);

				// We are done here. No need to check any more users.
				break;
			}
		}

		return (submissions, submissionMessageCount);
	}

	private static async Task<bool> IsMessageDisqualified(Guild? guild, RestMessage message, ReactionEmojiProperties? disqualificationEmoji)
	{
		if (disqualificationEmoji is null) return false;

		if (!message.HasBeenReactedToWith(disqualificationEmoji)) return false;

		await foreach(User user in message.GetReactionsAsync(disqualificationEmoji))
		{
			if (guild is not null)
			{
				GuildUser guildUser = await guild.GetUserAsync(user.Id);
				Permissions permissions = guildUser.GetPermissions(guild);
				if (permissions.HasFlag(Permissions.ManageMessages)) return true;
			}
			else
			{
				// We're not in a guild, so we're probably in a DM

				// Here, users may disqualify their own messages (dunno why, but let's do it)
				if (user == message.Author) return true;
			}
		}
		return false;
	}
}
