using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Net.Serialization;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;
using System.ComponentModel;

namespace GlyfiBot.Commands;

public class SetTheRoleCommand
{
	private const string ROLE_FILE = $"{Program.SETTINGS_DIR}/role.json";

	private static DiscordRole? _theRoleBacking = null;

	public static DiscordRole? TheRole
	{
		get => _theRoleBacking;
		private set
		{
			if (value is null)
			{
				File.Delete(ROLE_FILE);
			}
			else
			{
				File.WriteAllText(ROLE_FILE, DiscordJson.SerializeObject(value));
			}
			_theRoleBacking = value;
		}
	}

	public static void Load()
	{

		if (File.Exists(ROLE_FILE))
		{
			string jsonString = File.ReadAllText(ROLE_FILE);
			JToken jToken = JToken.Parse(jsonString);
			TheRole = jToken.ToDiscordObject<DiscordRole>();
			Console.WriteLine($"Loaded role to {TheRole}");
		}
		else
		{
			Console.WriteLine("Role has not been set.");
		}
	}

	[Command("set-role")]
	[Description("Set the role that can run the `/select` and `/set-emoji` commands")]
	[RequirePermissions([], [DiscordPermission.Administrator])]
	[UsedImplicitly]
	public static async ValueTask RoleAsync(SlashCommandContext context, DiscordRole role)
	{
		TheRole = role;
		string message = $"Role set to {role}";
		Console.WriteLine(message);
		await context.SendEphemeralResponse(message);
	}
}
