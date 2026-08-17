using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using System.Text;
using System.Text.RegularExpressions;
using Whisper.net;
using Whisper.net.Ggml;

namespace GlyfiBot.Services;

public static partial class VoiceToTextService
{
	private const string REMOVAL_EMOJI = "❌";
	private const string TRANSCRIPT_HEADER = "**Transcript:**";
	private const string TRANSCRIPT_FOOTER = $"-# Click the {REMOVAL_EMOJI} below to remove this transcript. (Only the original author can do this.)";

	// https://github.com/openai/whisper#available-models-and-languages
	private const GgmlType MODEL_TYPE = GgmlType.Small;
	// ReSharper disable once InconsistentNaming
	private static readonly string MODEL_PATH = $"{Program.VOICE_MODEL_DIR}/ggml-{(int)MODEL_TYPE}-{MODEL_TYPE.ToString().ToLowerInvariant()}.bin";

	private static Transcriber? _transcriber;
	private static GatewayClient _client = null!;

	public static async Task RunAsync(GatewayClient client)
	{
		_client = client;
		Directory.CreateDirectory(Program.VOICE_MODEL_DIR);
		try
		{
			await SetupModel();
			_transcriber = new Transcriber();
		}
		catch(Exception e)
		{
			Console.Error.WriteLine(e);
		}

		_client.MessageCreate += ProcessMessage;
		_client.MessageReactionAdd += ProcessReaction;
	}

	private static async ValueTask ProcessMessage(Message message)
	{
		if (message.Attachments.Count == 0) return;
		if (message.Author.IsBot) return;
		// if (!message.Flags.HasFlag(MessageFlags.IsVoiceMessage)) return; // Only process original voice messages. No arbitrary ogg or opus files.
		if (_transcriber is null) return;

		string downloadPath = Path.Join(Program.VOICE_TO_TEXT_RUNS_DIR, message.Id.ToString());
		List<string> downloads = await DownloadAttachments(downloadPath, message.Attachments);
		if (downloads.Count == 0) return;

		List<string> convertedFilePaths = await ConvertAudioFilesToWav(downloads);
		if (convertedFilePaths.Count == 0) return;

		List<string> transcripts = await TranscribeWavFiles(_transcriber, convertedFilePaths);
		if (transcripts.Count == 0) return;

		await SendTranscripts(transcripts, message);
	}

	private static async ValueTask ProcessReaction(MessageReactionAddEventArgs arg)
	{
		if (arg.MessageAuthorId != Program.BotUser.Id) return; // Only process messages sent by the bot.
		if (arg.Emoji.Name != REMOVAL_EMOJI) return; // Only process reactions with the removal emoji.
		if (arg.UserId == Program.BotUser.Id) return; // Ignore reactions from the bot itself.

		RestMessage transcriptionMessage = await _client.Rest.GetMessageAsync(arg.ChannelId, arg.MessageId);
		if (!transcriptionMessage.Content.StartsWith(TRANSCRIPT_HEADER)) return;

		RestMessage? originalMessage = transcriptionMessage.ReferencedMessage;
		if (originalMessage is null) return;

		if (originalMessage.Author.Id == arg.UserId)
		{
			await transcriptionMessage.DeleteAsync();
		}
		else
		{
			await transcriptionMessage.DeleteUserReactionAsync(new ReactionEmojiProperties(REMOVAL_EMOJI), arg.UserId);
		}
	}

	private class Transcriber
	{
		private readonly WhisperProcessor _whisperProcessor;
		// ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
		private readonly WhisperFactory _whisperFactory;

		public Transcriber()
		{
			_whisperFactory = WhisperFactory.FromPath(MODEL_PATH);
			_whisperProcessor = _whisperFactory.CreateBuilder().WithPrompt(
				"""
				Hello.
				You are an AI bot that transcribes spoken human language to written text.
				The input may be any language, but you *ALWAYS* _have_ to include proper punctuation, or bad things will happen…
				The language may change during speech! You have to handle this properly, and start transcribing in that new language.
				Do *NOT* write anything like "[speaking foreign language]"!
				If you transcribe anything as "[speaking foreign language]", bad things will also happen!
				If you _really_ don't know how to transcribe something, you may write "[gibberish]".
				Only do this as an ultimate last resort! You really have to try your best first. I believe in you, you can do it!
				""" //blegh 🤮
			).WithLanguageDetection().Build();
		}

		public async ValueTask<string> Transcribe(Stream waveStream)
		{
			StringBuilder sb = new();
			await foreach(SegmentData result in _whisperProcessor.ProcessAsync(waveStream))
			{
				sb.Append(result.Text);
			}
			return sb.ToString().Trim().RemoveStartingQuote();
		}
	}

#region Processing Steps

	private static async ValueTask<List<string>> DownloadAttachments(string downloadPath, IEnumerable<Attachment> attachmentFiles)
	{
		List<string> paths = [];

		uint antiDuplicateCounter = 0;
		foreach(Attachment attachmentFile in attachmentFiles)
		{
			string? contentType = attachmentFile.ContentType;
			if (contentType is null) continue;
			if (!contentType.StartsWith("audio/")) continue;
			// if (contentType is not ("audio/ogg" or "audio/opus")) continue; // Better filtering
			// if (attachmentFile is not VoiceAttachment) continue; // Possibly even better filtering

			string path = CreateDownloadFilePath();
			if (File.Exists(path))
			{
				antiDuplicateCounter++;
				path = CreateDownloadFilePath();
			}

			Directory.CreateDirectory(downloadPath); //only do it this late to not create a directory if there are no audio files to transcribe.
			{
				await using Stream networkStream = await Program.HttpClient.GetStreamAsync(attachmentFile.Url);
				await using FileStream fileStream = new(path, FileMode.CreateNew);
				await networkStream.CopyToAsync(fileStream);
			}

			paths.Add(path);
			continue;

			string CreateDownloadFilePath() => antiDuplicateCounter == 0
				? Path.Join(downloadPath, attachmentFile.FileName)
				: Path.Join(downloadPath, $"{Path.GetFileNameWithoutExtension(attachmentFile.FileName)} ({antiDuplicateCounter}){Path.GetExtension(attachmentFile.FileName)}");
		}

		return paths;
	}

	private static async ValueTask<List<string>> ConvertAudioFilesToWav(List<string> inputFilePaths)
	{
		List<string> convertedFilePaths = [];

		FfmpegService.FfmpegRunner? ffmpeg = FfmpegService.Ffmpeg;
		if (ffmpeg is null) return [];

		foreach(string fileToConvert in inputFilePaths)
		{
			string outputFile = Path.ChangeExtension(fileToConvert, "wav");
			await ffmpeg.Run("-i", fileToConvert, "-ar", "16000", outputFile);
			convertedFilePaths.Add(outputFile);
		}

		return convertedFilePaths;
	}

	private static async ValueTask<List<string>> TranscribeWavFiles(Transcriber transcriber, List<string> wavFilePaths)
	{
		List<string> transcripts = [];
		foreach(string wavFilePath in wavFilePaths)
		{
			await using FileStream fileStream = File.OpenRead(wavFilePath);
			string transcript = await transcriber.Transcribe(fileStream);
			transcripts.Add(transcript);
		}

		return transcripts;
	}

	private static async Task SendTranscripts(List<string> transcripts, Message message)
	{
		foreach(string transcript in transcripts)
		{
			RestMessage sentMessage;
			if (transcript.Length > 1800)
			{
				IReadOnlyCollection<AttachmentProperties> attachments = [new("transcript.txt", new MemoryStream(Encoding.UTF8.GetBytes(transcript.AddNewlines())))];
				sentMessage = await message.ReplyAsync(new ReplyMessageProperties
				{
					Content = $"""
					           {TRANSCRIPT_HEADER}
					           {TRANSCRIPT_FOOTER}
					           """,
					Attachments = attachments,
					AllowedMentions = AllowedMentionsProperties.None,
				});
			}
			else
			{
				sentMessage = await message.ReplyAsync(new ReplyMessageProperties
				{
					Content = $"""
					           {TRANSCRIPT_HEADER}
					           > {transcript}
					           {TRANSCRIPT_FOOTER}
					           """,
					AllowedMentions = AllowedMentionsProperties.None,
				});
			}
			await sentMessage.AddReactionAsync(new ReactionEmojiProperties(REMOVAL_EMOJI));
		}
	}

#endregion

#region Setup

	private static async Task SetupModel()
	{
		if (File.Exists(MODEL_PATH))
		{
			Console.WriteLine("Did not download Voice-to-Text model, as it's already present.");
			return;
		}

		Console.WriteLine("Downloading Voice-to-Text model...");
		await using Stream modelStream = await new WhisperGgmlDownloader(Program.HttpClient).GetGgmlModelAsync(MODEL_TYPE);
		await using FileStream fileWriter = File.OpenWrite(MODEL_PATH);
		await modelStream.CopyToAsync(fileWriter);
		Console.WriteLine("Finished downloading Voice-to-Text model!");
	}

#endregion

#region Utils

	extension(string input)
	{
		private string AddNewlines() => LineSplitter().Replace(input, match => $"{match.Groups[1].Value}\n");

		private string RemoveStartingQuote() => StartingQuoteRemover().Replace(input, string.Empty);
	}

	[GeneratedRegex("""(?<!\b(?:dr|mr|mx|mrs|ms|e\.?t\.?c|e\.?g|i\.?e)\b)([.?!]+['"]?)\s*""", RegexOptions.IgnoreCase, "en-GB")]
	private static partial Regex LineSplitter();

	[GeneratedRegex("""^>>\s*""")]
	private static partial Regex StartingQuoteRemover();

#endregion

}
