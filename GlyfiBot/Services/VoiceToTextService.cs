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
	// https://github.com/openai/whisper#available-models-and-languages
	private const GgmlType MODEL_TYPE = GgmlType.Small;
	// ReSharper disable once InconsistentNaming
	private static readonly string MODEL_PATH = $"{Program.VOICE_MODEL_DIR}/ggml-{(int)MODEL_TYPE}-{MODEL_TYPE.ToString().ToLowerInvariant()}.bin";

	private static Transcriber? _transcriber;

	public static async Task RunAsync(GatewayClient client)
	{
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

		client.MessageCreate += ProcessMessage;
	}

	private static async ValueTask ProcessMessage(Message message)
	{
		if (message.Attachments.Count == 0) return;
		if (message.Author.IsBot) return;
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

	private class Transcriber
	{
		private readonly WhisperProcessor _whisperProcessor;
		// ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
		private readonly WhisperFactory _whisperFactory;

		public Transcriber()
		{
			_whisperFactory = WhisperFactory.FromPath(MODEL_PATH);
			_whisperProcessor = _whisperFactory.CreateBuilder().WithLanguageDetection().Build();
		}

		public async ValueTask<string> Transcribe(Stream waveStream)
		{
			StringBuilder sb = new();
			await foreach(SegmentData result in _whisperProcessor.ProcessAsync(waveStream))
			{
				sb.Append(result.Text);
			}
			return sb.ToString().Trim();
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
			if (transcript.Length > 1000)
			{
				IReadOnlyCollection<AttachmentProperties> attachments = [new("transcript.txt", new MemoryStream(Encoding.UTF8.GetBytes(AddNewlines(transcript))))];
				await message.ReplyAsync(new ReplyMessageProperties
				{
					Content = "Transcript",
					Attachments = attachments,
				});
			}
			else
			{
				await message.ReplyAsync(new ReplyMessageProperties
				{
					Content = $"""
					           **Transcript:**

					           {transcript}
					           """,
				});
			}
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

	private static string AddNewlines(string input)
	{
		return LineSplitter().Replace(input, match => $"{match.Groups[1].Value}\n");
	}

	// [GeneratedRegex(@"(?<!\b(?:dr|mr|mx|mrs|ms|e\.?t\.?c|e\.?g|i\.?e)\b)([.?!]+)\s*", RegexOptions.IgnoreCase, "en-GB")]
	[GeneratedRegex(@"(?<!\b(?:dr|mr|mx|mrs|ms|e\.?t\.?c|e\.?g|i\.?e)\b)([.?!]+['""]?)\s*", RegexOptions.IgnoreCase, "en-GB")]
	private static partial Regex LineSplitter();

#endregion

}
