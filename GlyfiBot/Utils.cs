using DSharpPlus.Commands;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using System.Diagnostics.CodeAnalysis;

namespace GlyfiBot;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class Utils
{
	public static T? TryParseOrFallback<T>(this string? input, T? fallback) where T : IParsable<T>
	{
		return T.TryParse(input, null, out T? result)
			? result
			: fallback;
	}

	/// <summary>
	/// Gets all messages between two message IDs.
	/// </summary>
	///
	/// <param name="channel"><see cref="DiscordChannel"/> in which to look for the messages</param>
	/// <param name="start">ID of the message where to start getting (inclusive)</param>
	/// <param name="end">ID of the message where to end getting (inclusive)</param>
	///
	/// <returns>A <see cref="List{T}"/> of <see cref="DiscordMessage"/>s</returns>
	///
	/// <remarks>
	/// Before using this function, you should verify that both <c><see cref="start"/></c> and <c><see cref="end"/></c>
	/// are in the same channel with <see cref="GetMessageAsync"/> and that they are in the correct order.
	/// </remarks>
	public static async Task<List<DiscordMessage>> GetMessagesBetweenAsync(DiscordChannel channel, ulong start, ulong end)
	{
		// List<DiscordMessage> messages = [await channel.GetMessageAsync(start)]; //start with the first one, because `GetMessagesAfterAsync` does not include the first one
		List<DiscordMessage> messages = [];
		bool looping = true;
		ulong readHead = start - 1; //-1, because `GetMessagesAfterAsync` does not include the first one otherwise
		// ulong readHead = start;
		while(looping)
		{
			await foreach(DiscordMessage message in channel.GetMessagesAfterAsync(readHead))
			{
				messages.Add(message);
				if (message.Id == end)
				{
					looping = false;
					break;
				}
				readHead = message.Id;
			}
		}
		return messages;
	}

	/// <summary>
	/// Tries to get a message from the context.
	/// Sends an error response with <see cref="SendEphemeralResponseAsync"/> if the message cannot be got.
	/// </summary>
	///
	/// <param name="context">The <see cref="CommandContext"/></param>
	/// <param name="id">The message ID</param>
	///
	/// <returns>The <see cref="DiscordMessage"/> if it can be got, otherwise <c>null</c></returns>
	public static async ValueTask<DiscordMessage?> GetMessageAsync(CommandContext context, ulong id)
	{
		DiscordMessage msgStart;
		try
		{
			msgStart = await context.Channel.GetMessageAsync(id);
		}
		catch(UnauthorizedException)
		{
			await context.SendEphemeralResponseAsync("Message ID `{id}`: Unauthorized");
			return null;
		}
		catch(NotFoundException)
		{
			await context.SendEphemeralResponseAsync($"Message ID `{id}`: Not Found (in this channel)");
			return null;
		}
		catch(BadRequestException)
		{
			await context.SendEphemeralResponseAsync($"Message ID `{id}`:  Bad Request");
			return null;
		}
		catch(ServerErrorException)
		{
			await context.SendEphemeralResponseAsync($"Message ID `{id}`: Server Error");
			return null;
		}
		return msgStart;
	}

	/// <summary>
	/// Sends a message as an ephemeral message as a response to a command, through its context.
	/// </summary>
	///
	/// <param name="context">The <see cref="CommandContext"/></param>
	/// <param name="message">The contents of the message</param>
	/// <param name="filePath">An optional file path to attach to the message</param>
	public static async Task SendEphemeralResponseAsync(this CommandContext context, string message, string? filePath = null)
	{
		DiscordInteractionResponseBuilder interactionResponseBuilder = new DiscordInteractionResponseBuilder() //
			.WithContent(message) //
			.AsEphemeral() //
			.SuppressEmbeds();
		if (filePath is not null)
		{
			FileStream fileStream = new(filePath, FileMode.Open);
			interactionResponseBuilder = interactionResponseBuilder.AddFile(Path.GetFileName(filePath), fileStream);
		}
		await context.RespondAsync(interactionResponseBuilder);
	}

	/// <summary>
	/// Whether a specific member has a role or not
	/// </summary>
	///
	/// <param name="member">The member to check</param>
	/// <param name="role">The role to check</param>
	///
	/// <returns>Whether the <see cref="DiscordMember"/> has this <see cref="DiscordRole"/></returns>
	///
	/// <remarks>
	/// Includes extra handling for @everyone.
	/// </remarks>
	public static bool HasRole(this DiscordMember? member, DiscordRole? role)
	{
		//if there is no member, no roles can be had
		if (member is null) return false;

		//if there is no role, member can't have it
		if (role is null) return false;

		//role is everyone; of course member has that
		if (role == member.Guild.EveryoneRole) return true;

		//member has role
		return member.Roles.Contains(role);
	}

	/// <summary>
	/// Whether a specific message has been reacted to with this specific emoji.
	/// </summary>
	///
	/// <param name="message">The message</param>
	/// <param name="emoji">The emoji</param>
	///
	/// <returns>Whether it's happened or not</returns>
	public static bool HasBeenReactedToWith(this DiscordMessage message, DiscordEmoji emoji)
	{
		foreach(DiscordReaction reaction in message.Reactions)
		{
			if (reaction.Emoji == emoji)
			{
				return true;
			}
		}

		return false;
	}
}
