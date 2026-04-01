using JetBrains.Annotations;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using System.Numerics;
using Path = System.IO.Path;

namespace GlyfiBot.Commands;

public class VoteReactCommand : ApplicationCommandModule<SlashCommandContext>
{
	[SlashCommand("vote-react",
		"Automatically add voting reactions to a message",
		DefaultGuildPermissions = Permissions.Administrator)]
	[UsedImplicitly]
	public async Task ExecuteAsync(
		[SlashCommandParameter(Description = "The message(s) to add the reactions to. If multiple, separate with spaces")]
		string messages,
		[SlashCommandParameter(Description = "Amount of voting reactions to add")]
		int amount
	)
	{
		// This is going to take a moment
		await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

		// Parse messages and get references to them
		string[] messageIDs = messages.Split(" ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		int requiredMessagesForAmount = (int)Math.Floor((amount - 1) / 20.0) + 1;
		if (messageIDs.Length < requiredMessagesForAmount)
		{
			throw new SimpleCommandFailException($"{amount} reactions requires {requiredMessagesForAmount} messages, but only {messageIDs.Length} {(messageIDs.Length == 1 ? "was" : "were")} provided!");
		}

		RestMessage[] messagesToReactTo = new RestMessage[messageIDs.Length];
		for(int i = 0; i < messageIDs.Length; i++)
		{
			// Validate stringId
			string strMessageID = messageIDs[i];
			if (!ulong.TryParse(strMessageID, null, out ulong messageId))
			{
				throw new SimpleCommandFailException($"`{strMessageID} needs to be a number: the Message ID");
			}
			messagesToReactTo[i] = await Utils.VerifyThatMessageIsInChannel(Context, messageId);
		}

		// Get emojis
		IReadOnlyList<ApplicationEmoji> emojis = await Context.Client.Rest.GetApplicationEmojisAsync(Context.Client.Id);
		Dictionary<string, ApplicationEmoji> emojiNames = new();
		foreach(ApplicationEmoji applicationEmoji in emojis)
		{
			emojiNames[applicationEmoji.Name] = applicationEmoji;
		}

		// Get labels
		string typstExe = await TypstCommand.SetupTypst(Context);
		string scriptPath = await TypstCommand.SetupScript(Context);
		string scriptDir = TypstCommand.GetScriptDir(scriptPath);
		List<string> labels = await TypstCommand.GetLabels(typstExe, scriptDir);

		if (amount > labels.Count)
		{
			throw new SimpleCommandFailException($"There aren't enough labels defined to be able to react with {amount} emoji! We have only {labels.Count}.\n" +
				"You can add more here: https://github.com/glyphs-fi/weekly-challenges-typst/blob/main/global-config.typ");
		}

		// Set up for image generation
		const int imageSize = 128;
		const int textSize = 100;
		const int cornerRadius = 16;

		FontCollection collection = new();
		Font font = collection.Add(Path.Combine(scriptDir, "fonts", "Rubik", "Rubik [700 normal].ttf")).CreateFont(textSize);
		Font fallback = collection.Add(Path.Combine(scriptDir, "fonts", "Noto Sans", "Noto Sans [900 normal].ttf")).CreateFont(textSize);

		RichTextOptions textOptions = new(font)
		{
			Origin = new Vector2((int)(imageSize / 2.0), (int)(imageSize / 2.0)),
			FallbackFontFamilies = [fallback.Family],
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};

		Color background = Color.FromRgb(84, 153, 78);
		Color foreground = Color.FromRgb(255, 243, 225);

		// Start reacting!
		await Context.ModifyEphemeralResponseAsync("Reacting...");

		for(int i = 0; i < amount; i++)
		{
			string label = labels[i];
			string emojiName = $"vote_{Utils.HashDJB2(label)}";
			// Check if we already have an emoji for this label
			if (!emojiNames.TryGetValue(emojiName, out ApplicationEmoji? emoji))
			{
				// We don't, so we generate a new one and upload it
				using Image image = new Image<Rgba32>(imageSize, imageSize);
				image.Mutate(x => x.Fill(background).DrawText(textOptions, label, foreground).ApplyRoundedCorners(cornerRadius));
				using MemoryStream bytes = new();
				await image.SaveAsPngAsync(bytes);
				emoji = await Context.Client.Rest.CreateApplicationEmojiAsync(Context.Client.Id, new ApplicationEmojiProperties(emojiName, new ImageProperties(ImageFormat.Png, bytes.ToArray())));
			}

			// In the case of
			int messageIndex = (int)Math.Floor(i / 20.0);

			await messagesToReactTo[messageIndex].AddReactionAsync(new ReactionEmojiProperties(emojiName, emoji.Id));
		}

		await Context.ModifyEphemeralResponseAsync("Done!");
	}
}

// From https://github.com/SixLabors/Samples/blob/main/ImageSharp/AvatarWithRoundedCorner/Program.cs
static internal class ImgOps
{
	// This method can be seen as an inline implementation of an `IImageProcessor`:
	// (The combination of `IImageOperations.Apply()` + this could be replaced with an `IImageProcessor`)
	public static IImageProcessingContext ApplyRoundedCorners(this IImageProcessingContext context, float cornerRadius)
	{
		Size size = context.GetCurrentSize();
		IPathCollection corners = BuildCorners(size.Width, size.Height, cornerRadius);

		context.SetGraphicsOptions(new GraphicsOptions
		{
			Antialias = true,

			// Enforces that any part of this shape that has colour is punched out of the background
			AlphaCompositionMode = PixelAlphaCompositionMode.DestOut,
		});

		// Mutating in here as we already have a cloned original
		// use any colour (not Transparent), so the corners will be clipped
		foreach(IPath path in corners)
		{
			context = context.Fill(Color.Red, path);
		}

		return context;
	}

	private static PathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius)
	{
		// First create a square
		RectangularPolygon rect = new RectangularPolygon(-0.5f, -0.5f, cornerRadius, cornerRadius);

		// Then cut out of the square a circle so we are left with a corner
		IPath cornerTopLeft = rect.Clip(new EllipsePolygon(cornerRadius - 0.5f, cornerRadius - 0.5f, cornerRadius));

		// Corner is now a corner shape positions top left
		// let's make 3 more positioned correctly, we can do that by translating the original around the centre of the image.

		float rightPos = imageWidth - cornerTopLeft.Bounds.Width + 1;
		float bottomPos = imageHeight - cornerTopLeft.Bounds.Height + 1;

		// Move it across the width of the image - the width of the shape
		IPath cornerTopRight = cornerTopLeft.RotateDegree(90).Translate(rightPos, 0);
		IPath cornerBottomLeft = cornerTopLeft.RotateDegree(-90).Translate(0, bottomPos);
		IPath cornerBottomRight = cornerTopLeft.RotateDegree(180).Translate(rightPos, bottomPos);

		return new PathCollection(cornerTopLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
	}
}
