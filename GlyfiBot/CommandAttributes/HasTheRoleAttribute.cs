using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using GlyfiBot.Commands;

namespace GlyfiBot.CommandAttributes;

public class HasTheRoleAttribute : ContextCheckAttribute;

// It is instantiated by DSharpPlus
// ReSharper disable once ClassNeverInstantiated.Global
public class HasTheRoleCheck : IContextCheck<HasTheRoleAttribute>
{
	public ValueTask<string?> ExecuteCheckAsync(HasTheRoleAttribute attribute, CommandContext context)
	{
		return context.Member.HasRole(SetTheRoleCommand.TheRole)
			? ValueTask.FromResult<string?>(null)
			: ValueTask.FromResult<string?>("You do not have permission to do this.");
	}
}
