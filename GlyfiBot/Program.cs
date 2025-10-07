using GlyfiBot.Commands;
using NetCord;
using NetCord.Gateway;
using NetCord.Logging;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace GlyfiBot;

static internal class Program
{
	private const string DATA_DIR = "data";
	public const string SELECTIONS_DIR = $"{DATA_DIR}/selections";
	public const string SETTINGS_DIR = $"{DATA_DIR}/settings";

	private static async Task Main()
	{
		string? token = Environment.GetEnvironmentVariable("GLYFI_TOKEN");
		if (string.IsNullOrWhiteSpace(token))
		{
			Console.WriteLine("Error: No discord token found. Please provide a token via the GLYFI_TOKEN environment variable.");
			Environment.Exit(1);
		}

		Directory.CreateDirectory(DATA_DIR);
		Directory.CreateDirectory(SELECTIONS_DIR);
		Directory.CreateDirectory(SETTINGS_DIR);

		SetTheEmojiCommand.Load();

		GatewayClient client = new(
			new BotToken(token),
			new GatewayClientConfiguration
			{
				Intents = GatewayIntents.AllNonPrivileged | GatewayIntents.MessageContent,
				Logger = new ConsoleLogger(),
			}
		);

		ApplicationCommandService<SlashCommandContext> applicationCommandService = new();

		client.InteractionCreate += async interaction =>
		{
			if (interaction is not SlashCommandInteraction slashCommandInteraction)
				return;

			IExecutionResult result = await applicationCommandService.ExecuteAsync(new SlashCommandContext(slashCommandInteraction, client));

			if (result is IFailResult failResult)
			{
				await slashCommandInteraction.SendEphemeralResponseAsync(failResult.Message);
			}
		};

		applicationCommandService.AddModule<SelectRangeCommand>();
		applicationCommandService.AddModule<SetTheEmojiCommand>();
		applicationCommandService.AddModule<GetTheEmojiCommand>();

		await applicationCommandService.RegisterCommandsAsync(client.Rest, client.Id);

		string[] challenges = ["Glyph", "Ambigram"];
		string whichChallenge = challenges[new Random().Next(0, challenges.Length)];
		UserActivityProperties[] activity = [new($"the {whichChallenge} Challenge", UserActivityType.Competing)];

		await client.StartAsync(new PresenceProperties(UserStatusType.Online) {Activities = activity});

		if (client.Cache.Guilds.Count > 1)
		{
			Console.WriteLine("Error: The bot is in multiple Discord Servers. This is not supported.");
			Environment.Exit(1);
		}

		await Task.Delay(-1);
	}
}
