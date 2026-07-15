using JetBrains.Annotations;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Collections.Concurrent;
using System.Text.Json;

namespace GlyfiBot.Commands;

public class SetModPingCommand : ApplicationCommandModule<SlashCommandContext>
{
	private const string MOD_PINGS_FILE = $"{Program.SETTINGS_DIR}/mod_pings.json";

	/// GuildId, Full role ping (string)
	private static ConcurrentDictionary<ulong, string> _modPings = null!;

	public static string? GetModsPing(RestGuild guild)
	{
		return _modPings.GetValueOrDefault(guild.Id);
	}

	public static string? GetModsPing(ulong guildId)
	{
		return _modPings.GetValueOrDefault(guildId);
	}

	public static async Task Load()
	{
		if (File.Exists(MOD_PINGS_FILE))
		{
			await using FileStream fs = File.OpenRead(MOD_PINGS_FILE);
			Dictionary<ulong, string> dict = (await JsonSerializer.DeserializeAsync(fs, ToJson.Default.DictionaryUInt64String))!;
			_modPings = new ConcurrentDictionary<ulong, string>(dict);
		}
		else
		{
			_modPings = new ConcurrentDictionary<ulong, string>();
		}
	}

	[SlashCommand("set-mod-ping",
		"Set the mod role that will be pinged whenever the mods should be pinged",
		DefaultGuildPermissions = Permissions.Administrator)]
	[UsedImplicitly]
	public async Task ExecuteAsync(
		[SlashCommandParameter(Description = "Ping the moderator role here")]
		string? modsPing = null)
	{
		if (modsPing is null || modsPing.IsWhiteSpace())
		{
			RemoveModsRegistration();
			await Context.SendEphemeralResponseAsync("Cleared mod ping for this server. Remember to `/set-mod-ping` it to something again soon!");
		}
		else
		{
			AddModPingRegistration(modsPing);
			await Context.SendEphemeralResponseAsync($"Set mod ping for this server to `{modsPing}`");
		}
		await SaveModPings();
	}

	private void AddModPingRegistration(string modsPing)
	{
		Guild? guild = Context.Guild;
		if (guild is null) throw new SimpleCommandFailException("`guild` was null!?");
		_modPings.TryAdd(guild.Id, modsPing);
	}

	private void RemoveModsRegistration()
	{
		Guild? guild = Context.Guild;
		if (guild is null) return;
		_modPings.TryRemove(guild.Id, out _);
	}

	private static async Task SaveModPings()
	{
		Dictionary<ulong, string> dict = _modPings.ToDictionary();
		await using FileStream fs = File.Open(MOD_PINGS_FILE, FileMode.Create);
		await JsonSerializer.SerializeAsync(fs, dict, ToJson.Default.DictionaryUInt64String);
	}
}
