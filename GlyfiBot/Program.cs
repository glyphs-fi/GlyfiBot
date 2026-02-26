using GlyfiBot.Commands;
using GlyfiBot.Services;
using NetCord;
using NetCord.Gateway;
using NetCord.Logging;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using System.Reflection;
using System.Text;

namespace GlyfiBot;

static internal class Program
{
	private const string DATA_DIR = "data";
	public const string SELECTIONS_DIR = $"{DATA_DIR}/selections";
	public const string PFPS_DIR = $"{DATA_DIR}/pfps";
	public const string SETTINGS_DIR = $"{DATA_DIR}/settings";
	public const string TYPST_EXE_DIR = $"{DATA_DIR}/typst-exe";
	public const string TYPST_SCRIPT_DIR = $"{DATA_DIR}/typst-script";
	public const string ANNOUNCEMENTS_DIR = $"{DATA_DIR}/announcements";
	public const string SHOWCASES_DIR = $"{DATA_DIR}/showcases";
	public const string WINNERS_DIR = $"{DATA_DIR}/winners";

	private static async Task Main()
	{
		Directory.CreateDirectory(DATA_DIR);
		Directory.CreateDirectory(SETTINGS_DIR);

		// Token retrieval
		//  Attempt 1: Environment Variable
		string? token = Environment.GetEnvironmentVariable("GLYFI_TOKEN");

		string tokenFile = Path.Join(SETTINGS_DIR, "token.txt");
		bool shouldSaveToken = false;
		if (string.IsNullOrWhiteSpace(token))
		{
			Console.WriteLine($"No Discord Bot Token found in the GLYFI_TOKEN environment variable. Proceeding to look in {tokenFile}...");
			//  Attempt 2: Token file
			if (File.Exists(tokenFile))
			{
				token = (await File.ReadAllTextAsync(tokenFile)).Trim();
				Console.WriteLine($"Loaded Discord Bot Token from {tokenFile}");
			}
			else
			{
				//  Attempt 3: StdIn
				Console.WriteLine($"No Discord Bot Token found in {tokenFile}. Please paste in the Discord Bot Token here:");
				token = Console.ReadLine();
				if (string.IsNullOrWhiteSpace(token))
				{
					Console.WriteLine("Invalid Discord Bot Token. Stopping the bot.");
					Environment.Exit(1);
				}
				shouldSaveToken = true;
			}
		}

		Directory.CreateDirectory(SELECTIONS_DIR);
		Directory.CreateDirectory(PFPS_DIR);

		SetTheEmojiCommand.Load();

		GatewayClient client = new(
			new BotToken(token),
			new GatewayClientConfiguration
			{
				Intents = GatewayIntents.AllNonPrivileged | GatewayIntents.MessageContent,
				Logger = new ConsoleLogger(),
			}
		);

		if (shouldSaveToken)
		{
			Console.WriteLine($"Saving Discord Bot Token to {tokenFile}");
			await File.WriteAllTextAsync(tokenFile, token);
		}

		ApplicationCommandService<SlashCommandContext> applicationCommandService = new();

		client.InteractionCreate += async interaction =>
		{
			if (interaction is not SlashCommandInteraction slashCommandInteraction)
				return;

			IExecutionResult result = await applicationCommandService.ExecuteAsync(new SlashCommandContext(slashCommandInteraction, client));

			if (result is IFailResult failResult)
			{
				if (failResult is IExceptionResult exceptionResult)
				{
					Console.Error.WriteLine(exceptionResult.Exception);
					if (exceptionResult.Exception is SimpleCommandFailException)
					{
						await Respond(exceptionResult.Message);
					}
					else
					{
						string exceptionText = exceptionResult.Exception.ToString();
						if (exceptionText.Length > 1900)
						{
							await Respond(exceptionResult.Message, exceptionText);
						}
						else
						{
							await Respond($"{exceptionResult.Message}\n```\n{exceptionText}```");
						}
					}
				}
				else
				{
					// ReSharper disable once MethodHasAsyncOverload
					Console.Error.WriteLine(failResult.Message);
					await Respond(failResult.Message);
				}

				if (slashCommandInteraction.Data.Name == TypstCommand.COMMAND_NAME)
				{
					TypstCommand.EndAfterError();
				}

				async Task Respond(string message, string? textToSendAsAttachment = null)
				{
					try
					{
						await slashCommandInteraction.SendEphemeralResponseAsync(message,
							textToSendAsAttachment == null ? null : [new AttachmentProperties("exception.txt", new MemoryStream(Encoding.UTF8.GetBytes(textToSendAsAttachment)))]);
					}
					catch(RestException restException) when(restException.StatusCode == System.Net.HttpStatusCode.BadRequest)
					{
						await slashCommandInteraction.SendEphemeralFollowupMessageAsync(message,
							textToSendAsAttachment == null ? null : [new AttachmentProperties("exception.txt", new MemoryStream(Encoding.UTF8.GetBytes(textToSendAsAttachment)))]);
					}
				}
			}
		};

		applicationCommandService.AddModule<SelectRangeCommand>();
		applicationCommandService.AddModule<SetTheEmojiCommand>();
		applicationCommandService.AddModule<GetTheEmojiCommand>();
		applicationCommandService.AddModule<ProfilePicturesCommand>();
		applicationCommandService.AddModule<TypstCommand>();

		await applicationCommandService.RegisterCommandsAsync(client.Rest, client.Id);

		await client.StartAsync();

		if (client.Cache.Guilds.Count > 1)
		{
			Console.WriteLine("Error: The bot is in multiple Discord Servers. This is not supported.");
			Environment.Exit(1);
		}

		await client.Rest.ModifyCurrentApplicationAsync(options =>
		{
			Assembly? assembly = Assembly.GetEntryAssembly();
			AssemblyInformationalVersionAttribute? info = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
			string? gitHash = info?.InformationalVersion.Split('+').Last();
			options.Description = $"""
			                       Hi! I'm Glyfi, your local G&A bot :)

			                       Bot Information:
			                       Source: https://github.com/glyphs-fi/GlyfiBot
			                       Version: {gitHash}
			                       """;
		});

		await Task.WhenAll(
			ForeverService.RunAsync(),
			StatusChangerService.RunAsync(client)
		);
	}
}
