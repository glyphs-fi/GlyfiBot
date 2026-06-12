using NetCord.Services.ApplicationCommands;

namespace GlyfiBot;

public class ProgressTracker
{
	public const string GLYFI_IS_THINKING = "ꔷꔷꔷ   Glyfi is thinking...";
	private readonly SemaphoreSlim _inProgress = new(1, 1);

	public async Task Start(SlashCommandContext context)
	{
		// If nothing to wait for, we start immediately
		if (await _inProgress.WaitAsync(0)) return;

		// We are waiting
		await context.ModifyEphemeralResponseAsync("Waiting on another command run to complete first.");
		await _inProgress.WaitAsync();
		await Task.Delay(500); //wait a little extra, just to ensure everything has fully finished
		await context.ModifyEphemeralResponseAsync(GLYFI_IS_THINKING); //go back to thinking, which is the default deferred state
	}

	public void End()
	{
		_inProgress.Release();
	}
}
