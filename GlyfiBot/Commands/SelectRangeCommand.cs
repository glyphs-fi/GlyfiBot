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

		await this.Defer();

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
			string directoryToArchive = await DownloadSubmissionsAsync(Context.Interaction, submissions, sbList, downloadType);
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

	private static async Task<string> DownloadSubmissionsAsync(SlashCommandInteraction interaction, Dictionary<User, List<Sub>> submissions, StringBuilder sb, DownloadType downloadType)
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

		foreach((User author, List<Sub> submissionFiles) in submissions)
		{
			sb.AppendLine($"- {author.ToString()}");
			string submissionUserPath = Path.Join(rawPath, author.Id.ToString());
			Directory.CreateDirectory(submissionUserPath);
			uint antiDuplicateCounter = 0;
			for(int i = 0; i < submissionFiles.Count; i++)
			{
				Sub submissionFile = submissionFiles[i];
				sb.AppendLine($"  - {submissionFile.Url}");
				string path = CreateDownloadFilePath();
				if (File.Exists(path))
				{
					antiDuplicateCounter++;
					path = CreateDownloadFilePath();
				}

				{
					await using Stream networkStream = await Program.HttpClient.GetStreamAsync(submissionFile.Url);
					await using FileStream fileStream = new(path, FileMode.CreateNew);
					await networkStream.CopyToAsync(fileStream);
				}

				if (downloadType == DownloadType.Flat)
				{
					string flatFilename = author.Username + (submissionFiles.Count > 1 ? $"_{i}" : "") + Path.GetExtension(path);
					File.Copy(path, Path.Join(flatPath, flatFilename));
				}
				continue;

				string CreateDownloadFilePath() => antiDuplicateCounter == 0
					? Path.Join(submissionUserPath, submissionFile.FileName)
					: Path.Join(submissionUserPath, $"{Path.GetFileNameWithoutExtension(submissionFile.FileName)} ({antiDuplicateCounter}){Path.GetExtension(submissionFile.FileName)}");
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
			Url = new Uri(fromEmbed.Image?.ProxyUrl ?? fromEmbed.Thumbnail?.ProxyUrl ?? throw new InvalidOperationException("Embed ProxyUrl is missing"));
			FileName = fromEmbed.Title ?? Path.GetFileName(Url.AbsolutePath);
		}
	}

	/// <summary>
	/// Checks if a message has valid submissions.
	/// </summary>
	/// <param name="message">The message to check on</param>
	/// <param name="submissionEmoji">The submission emoji</param>
	/// <param name="guild">Optionally, the guild in which the message was sent (used for disqualification emoji permission checks)</param>
	/// <param name="disqualificationEmoji">Optionally, the disqualification emoji</param>
	/// <returns>A tuple with:
	/// - A boolean deciding whether the message is a valid submission at all,<br/>
	/// - The problem string will be null if there are no problems or warnings. When there are, the problem is returned.<br/>
	/// Note that a valid submission can still have a problem (in which case it's more like a warning).<br/>
	/// - A list of valid submissions.
	/// </returns>
	public static async Task<(bool valid, string? problem, List<Sub> subs)> ValidateMessage(RestMessage message, ReactionEmojiProperties submissionEmoji, RestGuild? guild, ReactionEmojiProperties? disqualificationEmoji)
	{
		// Does this message even contain anything that could be a submission?
		if (message.Attachments.Count == 0 && message.Embeds.Count == 0) return (false, "No attachments or embeds", []);

		// Has this message been marked as a submission, through the reaction emoji?
		if (!message.HasBeenReactedToWith(submissionEmoji)) return (false, "The message wasn't reacted to with the submission emoji", []);

		// Has the user who submitted this message reacted to it with the submission emoji _themselves_? (The check above↑ only checks if the emoji is present at all)
		if (!await message.HasBeenReactedToWithBy(submissionEmoji, message.Author)) return (false, "The message author has not reacted to their own message with the submission emoji", []);

		// Is the message disqualified, through the disqualification reaction emoji?
		if (await IsMessageDisqualified(guild, message, disqualificationEmoji)) return (false, "Submission is disqualified", []);

		// Check attachments (direct uploads)
		List<Attachment> validAttachments = [];
		foreach(Attachment attachment in message.Attachments)
		{
			string? type = attachment.ContentType;
			if (type is null) continue; // Don't know when this would happen, but let's just ignore it...

			if (type.StartsWith("image/"))
			{
				validAttachments.Add(attachment);
			}
			else
			{
				if (!type.StartsWith("video/")) // We allow videos in the same message, along with actual image submissions
				{
					return (false, "Message contained an attachment that was not an image", []);
				}
			}
		}

		// Check embeds (includes content that might be linked, instead of uploaded)
		List<Embed> validEmbeds = [];
		foreach(Embed embed in message.Embeds)
		{
			EmbedType? type = embed.Type;
			if (type == EmbedType.Image)
			{
				if ((embed.Image?.ProxyUrl ?? embed.Thumbnail?.ProxyUrl) is null)
				{
					return (false, "Embed does not have a Thumbnail ProxyUrl", []);
				}
				validEmbeds.Add(embed);
			}
			else
			{
				// We allow videos in the same message, along with actual image submissions.
				// We also allow (ignore) the following:
				// - Article, for example: ArtStation and GitHub embeds
				// - Link, for example: technicjelle.com
				// - Rich, for example: a twitter.com tweet
				if (type is EmbedType.Video or EmbedType.Article or EmbedType.Link or EmbedType.Rich) continue;

				return (false, $"Message contained an embed that was not an image (`EmbedType.{type}`)", []);
			}
		}

		// Combine attachments and embeds into submissions
		List<Sub> submissions =
		[
			.. validAttachments.Select(attachment => new Sub(attachment)),
			.. validEmbeds.Select(embed => new Sub(embed)),
		];

		if (submissions.Count == 0)
		{
			return (false, "No valid submissions found in this message", []);
		}

		return (true, submissions.Count == 1 ? null : "_All_ images in this message will be counted as (separate) submissions!", submissions);
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
			(bool valid, _, List<Sub> messageSubmissions) = await ValidateMessage(message, emoji, guild, disqualificationEmoji);
			if (valid && messageSubmissions.Count > 0)
			{
				submissionMessageCount++;

				// Ensure that the `submissions` dictionary has an entry for this author...
				if (!submissions.TryGetValue(message.Author, out List<Sub>? authorSubs))
				{
					submissions[message.Author] = authorSubs = [];
				}

				// ...and then add all the submissions to the dictionary
				authorSubs.AddRange(messageSubmissions);
			}
		}

		return (submissions, submissionMessageCount);
	}

	private static async Task<bool> IsMessageDisqualified(RestGuild? guild, RestMessage message, ReactionEmojiProperties? disqualificationEmoji)
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
				// We're probably not in a guild, so we're probably in a DM
				// (If we _are_ in a Guild, then it's still fine to let users disqualify their own messages, I guess...)
				// Not good that then users with ManageMessages can't disqualify anymore, so not ideal.

				// Here, users may disqualify their own messages (dunno why, but let's do it)
				if (user.Id == message.Author.Id) return true;
			}
		}
		return false;
	}
}
