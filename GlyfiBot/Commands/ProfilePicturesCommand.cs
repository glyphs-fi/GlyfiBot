using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.IO.Compression;
using System.Text;

namespace GlyfiBot.Commands;

public enum DownloadFormat
{
	Original,
	PNG,
	Jpeg,
	WebP,
}
public enum AnimatedDownloadFormat
{
	Original,
	WebP,
}
public class ProfilePicturesCommand : ApplicationCommandModule<SlashCommandContext>
{
	[SlashCommand("pfps",
		"Get the profile pictures of one or multiple users in bulk",
		DefaultGuildPermissions = Permissions.Administrator)]
	[UsedImplicitly]
	public async Task ExecuteAsync(
		[SlashCommandParameter(Description = "The users of whom you want to download their profile pictures")]
		string userPings,
		//
		[SlashCommandParameter(Description = "The image format for non-animated profile pictures")]
		DownloadFormat downloadFormat = DownloadFormat.PNG,
		//
		[SlashCommandParameter(Description = "Do you want animated profile pictures, or should they all be non-animated?")]
		bool downloadAnimated = false,
		//
		[SlashCommandParameter(Description = "The image format for animated profile pictures")]
		AnimatedDownloadFormat animatedDownloadFormat = AnimatedDownloadFormat.WebP
	)
	{
		string[] splitUsers = userPings.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		HashSet<ulong> userIds = [];
		foreach(string user in splitUsers)
		{
			if (ulong.TryParse(user.TrimStart('<').TrimEnd('>').TrimStart('@'), out ulong userId))
			{
				userIds.Add(userId);
			}
			else
			{
				await Context.SendEphemeralResponseAsync($"Input `{user}` could not be parsed");
				return;
			}
		}

		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

		User[] users = await Task.WhenAll(userIds.Select(userId => Context.Client.Rest.GetUserAsync(userId)));

		StringBuilder sbStats = new();
		sbStats.AppendLine($"Selected users: {users.Length}");

		string? pfpsArchivePath = null;
		StringBuilder sbList = new();
		if (users.Length > 0)
		{
			string directoryToArchive = await DownloadPfpsAsync(Context.Interaction, users, sbList, downloadFormat, downloadAnimated, animatedDownloadFormat);
			pfpsArchivePath = Path.Join(Path.GetDirectoryName(directoryToArchive), $"{Path.GetFileName(directoryToArchive)}.zip");
			await ZipFile.CreateFromDirectoryAsync(directoryToArchive, pfpsArchivePath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
		}

		string stats = sbStats.ToString();
		string userList = sbList.ToString();
		try
		{
			await ModifyResponseAsync(msg =>
			{
				msg.Flags = MessageFlags.Ephemeral;

				List<AttachmentProperties> attachments = [];

				if (stats.Length + userList.Length >= 1950)
				{
					msg.Content = stats;
					attachments.Add(new AttachmentProperties("userlist.txt", new MemoryStream(Encoding.UTF8.GetBytes(userList))));
				}
				else
				{
					msg.Content = stats + "\n" + userList;
				}

				if (pfpsArchivePath is not null)
				{
					attachments.Add(new AttachmentProperties(Path.GetFileName(pfpsArchivePath), new FileStream(pfpsArchivePath, FileMode.Open)));
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

	private static async Task<string> DownloadPfpsAsync(SlashCommandInteraction interaction, User[] users, StringBuilder sb, DownloadFormat downloadFormat, bool downloadAnimated, AnimatedDownloadFormat animatedDownloadFormat)
	{
		string pfpsPath = Path.Join(Program.PFPS_DIR, interaction.Id.ToString());
		Directory.CreateDirectory(pfpsPath);

		using HttpClient client = new();
		foreach(User user in users)
		{
			sb.AppendLine($"- {user.ToString()}");
			DownloadFile downloadFile = GetAvatar(user, downloadFormat, downloadAnimated, animatedDownloadFormat);
			string path = Path.Join(pfpsPath, downloadFile.Filename);
			await using Stream networkStream = await client.GetStreamAsync(downloadFile.DownloadUrl);
			await using FileStream fileStream = new(path, FileMode.CreateNew);
			await networkStream.CopyToAsync(fileStream);
		}

		return pfpsPath;
	}

	private record DownloadFile(string Filename, string DownloadUrl);

	private static DownloadFile GetAvatar(User user, DownloadFormat downloadFormat, bool downloadAnimated, AnimatedDownloadFormat animatedDownloadFormat)
	{
		if (downloadAnimated)
		{
			ImageUrl url = user.AlwaysGetAvatarUrl();

			if (url.IsAnimated())
			{
				url = animatedDownloadFormat switch
				{
					AnimatedDownloadFormat.Original => user.AlwaysGetAvatarUrl(),
					AnimatedDownloadFormat.WebP => user.AlwaysGetAvatarUrl(ImageFormat.WebP),
					_ => throw new ArgumentOutOfRangeException(nameof(animatedDownloadFormat), animatedDownloadFormat, null),
				};
				return new DownloadFile($"{user.Username}{url.GetExtension()}", $"{url.ToString(4096)}&animated=true");
			}
			else
			{
				url = downloadFormat switch
				{
					DownloadFormat.Original => user.AlwaysGetAvatarUrl(),
					DownloadFormat.PNG => user.AlwaysGetAvatarUrl(ImageFormat.Png),
					DownloadFormat.Jpeg => user.AlwaysGetAvatarUrl(ImageFormat.Jpeg),
					DownloadFormat.WebP => user.AlwaysGetAvatarUrl(ImageFormat.WebP),
					_ => throw new ArgumentOutOfRangeException(nameof(downloadFormat), downloadFormat, null),
				};
				return new DownloadFile($"{user.Username}{url.GetExtension()}", url.ToString(4096));
			}
		}
		else
		{
			ImageUrl url = downloadFormat switch
			{
				DownloadFormat.Original => user.AlwaysGetAvatarUrl(),
				DownloadFormat.PNG => user.AlwaysGetAvatarUrl(ImageFormat.Png),
				DownloadFormat.Jpeg => user.AlwaysGetAvatarUrl(ImageFormat.Jpeg),
				DownloadFormat.WebP => user.AlwaysGetAvatarUrl(ImageFormat.WebP),
				_ => throw new ArgumentOutOfRangeException(nameof(downloadFormat), downloadFormat, null),
			};

			//in this else, we didn't want animated images, so if the url is original, but will end up getting an animated image, we override them to png
			if (downloadFormat == DownloadFormat.Original && url.IsAnimated())
			{
				url = user.AlwaysGetAvatarUrl(ImageFormat.Png);
			}
			return new DownloadFile($"{user.Username}{url.GetExtension()}", url.ToString(4096));
		}
	}
}
