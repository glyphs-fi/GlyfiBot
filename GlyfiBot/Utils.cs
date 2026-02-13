using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace GlyfiBot;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
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
	/// are in the same channel with <see cref="GetMessageAsync"/> and that they are in the correct order.
	/// </remarks>
	public static async Task<List<RestMessage>> GetMessagesBetweenAsync(SlashCommandContext context, ulong start, ulong end)
	{
		IAsyncEnumerable<RestMessage> asyncEnumerable = context.Client.Rest.GetMessagesAsync(
			context.Channel.Id,
			new PaginationProperties<ulong>
			{
				Direction = PaginationDirection.After,
				From = start - 1,
			});
		return await asyncEnumerable.WhereAsync(message => message.Id <= end).ToListAsync();
	}

	/// <summary>
	/// Tries to get a message from the context.
	/// Sends an error response with <see cref="SendEphemeralResponseAsync(SlashCommandContext, string)"/> if the message cannot be got.
	/// </summary>
	///
	/// <param name="context">The <see cref="CommandContext"/></param>
	/// <param name="messageId">The message ID</param>
	///
	/// <returns>The <see cref="RestMessage"/> if it can be got, otherwise <c>null</c></returns>
	public static async ValueTask<RestMessage?> GetMessageAsync(SlashCommandContext context, ulong messageId)
	{
		try
		{
			return await context.Client.Rest.GetMessageAsync(context.Channel.Id, messageId);
		}
		catch(RestException e)
		{
			await context.SendEphemeralResponseAsync($"Error on message `{messageId}`: {e.Error?.Message}");
			return null;
		}
	}

	/// <summary>
	/// Sends a message as an ephemeral message as a response to a command, through its interaction.
	/// </summary>
	///
	/// <param name="interaction">The <see cref="Interaction"/></param>
	/// <param name="content">The contents of the message</param>
	public static async Task SendEphemeralResponseAsync(this Interaction interaction, string content)
	{
		await interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
		{
			Content = content,
			Flags = MessageFlags.Ephemeral,
		}));
	}

	/// <summary>
	/// Sends a message as an ephemeral message as a response to a command, through its context.
	/// </summary>
	///
	/// <param name="context">The <see cref="CommandContext"/></param>
	/// <param name="content">The contents of the message</param>
	public static async Task SendEphemeralResponseAsync(this SlashCommandContext context, string content)
	{
		await context.Interaction.SendEphemeralResponseAsync(content);
	}

	/// <summary>
	/// Sends a followup message as an ephemeral message as a response to a command, through its interaction.
	/// </summary>
	/// <param name="interaction">The <see cref="Interaction"/></param>
	/// <param name="content">The contents of the message</param>
	public static async Task SendEphemeralFollowupMessageAsync(this Interaction interaction, string content)
	{
		await interaction.SendFollowupMessageAsync(new InteractionMessageProperties
		{
			Content = content,
			Flags = MessageFlags.Ephemeral,
		});
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

	public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> enumerable)
	{
		List<T> list = [];

		await foreach(T item in enumerable)
		{
			list.Add(item);
		}

		return list;
	}

	public static async IAsyncEnumerable<T> WhereAsync<T>(this IAsyncEnumerable<T> enumerable, Predicate<T> condition)
	{
		await foreach(T item in enumerable)
		{
			if (condition(item))
			{
				yield return item;
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

	/// <remarks>
	/// Includes the <c>.</c> of the extension.
	/// </remarks>
	public static string GetExtension(this ImageUrl imageUrl)
	{
		return Path.GetExtension(imageUrl.ToString());
	}

	public static bool IsAnimated(this ImageUrl imageUrl)
	{
		return imageUrl.GetExtension() == ".gif";
	}
}
