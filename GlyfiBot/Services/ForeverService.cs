namespace GlyfiBot.Services;

public static class ForeverService
{
	public static async Task RunAsync()
	{
		await Task.Delay(-1);
	}
}
