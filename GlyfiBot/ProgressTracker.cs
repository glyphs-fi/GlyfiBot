using NetCord.Services.ApplicationCommands;

namespace GlyfiBot;

public class ProgressTracker
{
	private bool _inProgress = false;

	public async Task Start(SlashCommandContext context)
	{
		if (_inProgress)
		{
			await context.ModifyEphemeralResponseAsync("Waiting on another command run to complete first.");
			while(_inProgress) await Task.Delay(500);
			_inProgress = true;
			await Task.Delay(500); //wait a little extra, just to ensure everything has fully finished
		}
		else
		{
			_inProgress = true;
		}
	}

	public void End()
	{
		_inProgress = false;
	}
}
