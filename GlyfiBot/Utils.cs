using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace GlyfiBot;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public static class Utils
{
	/// <summary>
	/// Gets all messages between two message IDs.
	/// </summary>
	///
	/// <param name="context"><see cref="SlashCommandContext"/> in which to look for the messages</param>
	/// <param name="start">ID of the message where to start getting (inclusive)</param>
	/// <param name="end">ID of the message where to end getting (inclusive)</param>
	///
	/// <returns>A <see cref="List{T}"/> of <see cref="RestMessage"/>s</returns>
	///
	/// <remarks>
	/// Before using this function, you should verify that both <c><see cref="start"/></c> and <c><see cref="end"/></c>
	/// are in the same channel with <see cref="VerifyThatMessageIsInChannel"/> and that they are in the correct order.
	/// </remarks>
	public static async Task<List<RestMessage>> GetMessagesBetweenAsync(SlashCommandContext context, ulong start, ulong? end)
	{
		IAsyncEnumerable<RestMessage> asyncEnumerable = context.Client.Rest.GetMessagesAsync(
			context.Channel.Id,
			new PaginationProperties<ulong>
			{
				Direction = PaginationDirection.After,
				From = start - 1,
			});
		return end == null
			? await asyncEnumerable.ToListAsync()
			: await asyncEnumerable.WhereAsync(message => message.Id <= end).ToListAsync();
	}

	/// <summary>
	/// Ensures that the message with this ID is in this channel (will throw a <see cref="SimpleCommandFailException"/> if not)
	/// </summary>
	///
	/// <param name="context">The <see cref="CommandContext"/></param>
	/// <param name="messageId">The message ID</param>
	public static async Task VerifyThatMessageIsInChannel(SlashCommandContext context, ulong messageId)
	{
		try
		{
			await context.Client.Rest.GetMessageAsync(context.Channel.Id, messageId);
		}
		catch(RestException)
		{
			throw new SimpleCommandFailException($"Message with ID `{messageId}` is not in this channel!");
		}
	}

	extension(Interaction interaction)
	{
		/// <summary>
		/// Sends a message as an ephemeral message as a response to a command, through its interaction.
		/// </summary>
		/// <param name="content">The contents of the message</param>
		public async Task SendEphemeralResponseAsync(string content)
		{
			await interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
			{
				Flags = MessageFlags.Ephemeral,
				Content = content,
			}));
		}


		/// <summary>
		/// Modifies a message as an ephemeral message as a response to a command, through its interaction.
		/// </summary>
		/// <param name="content">The contents of the message</param>
		public async Task ModifyEphemeralResponseAsync(string content)
		{
			await interaction.ModifyResponseAsync(msg =>
			{
				msg.Flags = MessageFlags.Ephemeral;
				msg.Content = content;
			});
		}

		/// <summary>
		/// Sends a followup message as an ephemeral message as a response to a command, through its interaction.
		/// </summary>
		/// <param name="content">The contents of the message</param>
		public async Task SendEphemeralFollowupMessageAsync(string content)
		{
			await interaction.SendFollowupMessageAsync(new InteractionMessageProperties
			{
				Flags = MessageFlags.Ephemeral,
				Content = content,
			});
		}
	}

	extension(SlashCommandContext context)
	{
		/// <summary>
		/// Sends a message as an ephemeral message as a response to a command, through its context.
		/// </summary>
		/// <param name="content">The contents of the message</param>
		public async Task SendEphemeralResponseAsync(string content)
		{
			await context.Interaction.SendEphemeralResponseAsync(content);
		}

		/// <summary>
		/// Modifies the response to the provided content, while setting/keeping the response ephemeral.
		/// </summary>
		/// <param name="content">The contents of the message</param>
		public async Task ModifyEphemeralResponseAsync(string content)
		{
			await context.Interaction.ModifyEphemeralResponseAsync(content);
		}
	}

	/// <summary>
	/// Whether a specific message has been reacted to with this specific emoji.
	/// </summary>
	///
	/// <param name="message">The message</param>
	/// <param name="emoji">The emoji</param>
	///
	/// <returns>Whether it's happened or not</returns>
	public static bool HasBeenReactedToWith(this RestMessage message, ReactionEmojiProperties emoji)
	{
		ulong? id = emoji.Id;
		string name = emoji.Name;

		foreach(MessageReaction messageReaction in message.Reactions)
		{
			ulong? reactionId = messageReaction.Emoji.Id;
			string? reactionName = messageReaction.Emoji.Name;

			if (id is not null && reactionId is not null)
			{
				if (id == reactionId) return true;
			}
			else
			{
				if (name == reactionName) return true;
			}
		}

		return false;
	}

	extension<T>(IAsyncEnumerable<T> enumerable)
	{
		public async Task<List<T>> ToListAsync()
		{
			List<T> list = [];

			await foreach(T item in enumerable)
			{
				list.Add(item);
			}

			return list;
		}

		public async IAsyncEnumerable<T> WhereAsync(Predicate<T> condition)
		{
			await foreach(T item in enumerable)
			{
				if (condition(item))
				{
					yield return item;
				}
			}
		}
	}

	public static GuildEmoji? GetEmojiByName(this Guild? guild, string emojiName)
	{
		return guild?.Emojis.Values.FirstOrDefault(emoji => emoji.Name == emojiName);
	}

	public static string String(this ReactionEmojiProperties emoji)
	{
		return $"`{emoji.Name}:{emoji.Id}`";
	}

	public static string? FirstEmoji(string s)
	{
		//Source: https://stackoverflow.com/a/75146758/8109619
		string e = StringInfo.GetNextTextElement(s);
		Rune r = e.EnumerateRunes().First();
		return Rune.IsSymbol(r) ? e : null;
	}

	public static bool HasInternalError(this RestException e, string errorKey)
	{
		RestError? restError = e.Error;
		IRestErrorGroup? iRestErrorGroup = restError?.Error;
		if (iRestErrorGroup is not RestErrorGroup restErrorGroup) return false;
		IRestErrorGroup? errorGroup = restErrorGroup.Errors.GetValueOrDefault("content");
		return errorGroup is RestErrorDetailGroup group && group.Errors.Any(restErrorDetail => restErrorDetail.Code == errorKey);
	}

	public static ImageUrl AlwaysGetAvatarUrl(this User user, ImageFormat? format = null)
	{
		ImageUrl? avatarUrl = user.GetAvatarUrl(format);
		return avatarUrl ?? user.DefaultAvatarUrl;
	}

	extension(ImageUrl imageUrl)
	{
		/// <remarks>
		/// Includes the <c>.</c> of the extension.
		/// </remarks>
		public string GetExtension()
		{
			return Path.GetExtension(imageUrl.ToString());
		}

		public bool IsAnimated()
		{
			return imageUrl.GetExtension() == ".gif";
		}
	}


	/// <summary>
	/// Extracts the archive at the provided path into its parent directory.
	/// </summary>
	/// <param name="archivePath"></param>
	/// <exception cref="DirectoryNotFoundException"></exception>
	public static async Task ExtractArchive(string archivePath)
	{
		DirectoryInfo? directoryInfo = Directory.GetParent(archivePath);
		if (directoryInfo is null) throw new DirectoryNotFoundException($"Parent not found: {archivePath}");

		string targetDir = directoryInfo.FullName;
		if (archivePath.EndsWith(".zip"))
		{
			await ZipFile.ExtractToDirectoryAsync(archivePath, targetDir);
		}
		else if (archivePath.EndsWith(".tar.xz"))
		{
			Process unpacker = new() {StartInfo = new ProcessStartInfo("tar", ["xf", archivePath, "-C", targetDir])};
			unpacker.Start();
			await unpacker.WaitForExitAsync();
		}
	}

	/// <summary>
	/// Finds an exe in the provided directory, or a subdirectory of it.
	/// </summary>
	/// <param name="searchPath">The directory in which to look (and its subdirectories)</param>
	/// <param name="exeName">The name of the executable to look for. Do not include ".exe" as that will automatically be added if necessary!</param>
	/// <returns></returns>
	public static string? FindExe(string searchPath, string exeName)
	{
		string[] files = Directory.GetFiles(searchPath, $"{exeName}*", new EnumerationOptions
		{
			RecurseSubdirectories = true,
			MaxRecursionDepth = 1,
			MatchCasing = MatchCasing.CaseInsensitive,
			MatchType = MatchType.Simple,
		});
		return files.FirstOrDefault(file => file.EndsWith(".exe") || Path.GetFileName(file) == exeName);
	}

	extension(string str)
	{
		public string UpperFirst() => $"{str[..1].ToUpperInvariant()}{str[1..]}";
		public string LowerFirst() => $"{str[..1].ToLowerInvariant()}{str[1..]}";
	}
}
public class SimpleCommandFailException(string message) : Exception(message);
