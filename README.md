# Glyfi Bot

Discord bot for the Glyphs & Alphabets server.

The goal of this project is to encourage very collaborative development on this bot.

## Current functionality:

- `/emoji`: See the currently set emoji that marks something as a submission.
- `/select`: Provide the IDs of two messages in the channel between where to look for submissions.  
  Submissions will be collected and provided in one message, for extra easy downloading!
- `/set-emoji`: Set the emoji that will mark something as a submission.
- `/set-role`: Set the role that can run the `/select` and `/set-emoji` commands.

## Tech Stack

For the Programming Language, I chose [C#](https://dotnet.microsoft.com/en-us/languages/csharp), because it is relatively accessible for any programmer.

For the Discord Library, I chose [DSharpPlus](https://github.com/DSharpPlus/DSharpPlus), because it looked pretty good.
