using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.InteractionNamingPolicies;
using DSharpPlus.Entities;
using GlyfiBot.CommandAttributes;
using GlyfiBot.Commands;

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
		SetTheRoleCommand.Load();

		DiscordClientBuilder clientBuilder = DiscordClientBuilder.CreateDefault(token, SlashCommandProcessor.RequiredIntents);

		clientBuilder.UseCommands((IServiceProvider provider, CommandsExtension extension) =>
		{
			SlashCommandProcessor slashCommandProcessor = new(new SlashCommandConfiguration
			{
				NamingPolicy = new KebabCaseNamingPolicy(),
			});
			extension.AddProcessor(slashCommandProcessor);
			extension.AddCheck<HasTheRoleCheck>();
			extension.AddCommands([
				typeof(SelectRangeCommand),
				typeof(SetTheEmojiCommand),
				typeof(GetEmojiCommand),
				typeof(SetTheRoleCommand),
			]);
		});
		DiscordClient client = clientBuilder.Build();

		DiscordActivity status = new("the Glyph Challenge", DiscordActivityType.Competing);

		await client.ConnectAsync(status, DiscordUserStatus.Online);

		await Task.Delay(-1);
	}
}
