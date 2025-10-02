using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Net.Serialization;
using GlyfiBot.Commands;
using Newtonsoft.Json.Linq;

namespace GlyfiBot;

static internal class Program
{
	private const string DATA_DIR = "data";
	private const string EMOJI_FILE = $"{DATA_DIR}/emoji.json";
	public const string SELECTIONS_DIR = $"{DATA_DIR}/selections";

	private static DiscordEmoji? _theEmojiBacking = null;

	public static DiscordEmoji? TheEmoji
	{
		get => _theEmojiBacking;
		set
		{
			if (value is null)
			{
				File.Delete(EMOJI_FILE);
			}
			else
			{
				File.WriteAllText(EMOJI_FILE, DiscordJson.SerializeObject(value));
			}
			_theEmojiBacking = value;
		}
	}

	private static async Task Main(string[] args)
	{
		string? token = Environment.GetEnvironmentVariable("GLYFI_TOKEN");
		if (string.IsNullOrWhiteSpace(token))
		{
			Console.WriteLine("Error: No discord token found. Please provide a token via the GLYFI_TOKEN environment variable.");
			Environment.Exit(1);
		}

		Directory.CreateDirectory(DATA_DIR);
		Directory.CreateDirectory(SELECTIONS_DIR);

		DiscordClientBuilder clientBuilder = DiscordClientBuilder.CreateDefault(token, SlashCommandProcessor.RequiredIntents);

		clientBuilder.UseCommands((IServiceProvider provider, CommandsExtension extension) =>
		{
			SlashCommandProcessor slashCommandProcessor = new(new SlashCommandConfiguration());
			extension.AddProcessor(slashCommandProcessor);
			extension.AddCommands([typeof(SelectRangeCommand), typeof(SetEmojiCommand), typeof(GetEmojiCommand)]);
		}, new CommandsConfiguration
		{
			RegisterDefaultCommandProcessors = true,
			DebugGuildId = Environment.GetEnvironmentVariable("DEBUG_GUILD_ID").TryParseOrFallback(0ul),
		});

		if (File.Exists(EMOJI_FILE))
		{
			string jsonString = await File.ReadAllTextAsync(EMOJI_FILE);
			JToken jToken = JToken.Parse(jsonString);
			TheEmoji = jToken.ToDiscordObject<DiscordEmoji>();
			Console.WriteLine($"Loaded emoji to {TheEmoji}");
		}

		DiscordClient client = clientBuilder.Build();

		DiscordActivity status = new("the Glyph Challenge", DiscordActivityType.Competing);

		await client.ConnectAsync(status, DiscordUserStatus.Online);

		await Task.Delay(-1);
	}
}
