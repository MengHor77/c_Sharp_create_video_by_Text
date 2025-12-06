using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Speech.Synthesis;
using NAudio.Wave;
using System.Drawing;

namespace c_Sharp_video
{
    class Program
    {
        static string ffmpegPath = @"D:\c_Sharp_video\c_Sharp_video\ffmpeg-2025-12-01-git-7043522fe0-full_build\bin\ffmpeg.exe";
        static void Main(string[] args)
        {
            string txtFile = @"D:\c_Sharp_video\c_Sharp_video\When_Failure_Became_My_Teacher.txt";
            string imageFile = @"D:\c_Sharp_video\c_Sharp_video\image.png";
            string mp3File = @"D:\c_Sharp_video\c_Sharp_video\audio.mp3";
            string srtFile = @"D:\c_Sharp_video\c_Sharp_video\video.srt";
            string tempVideo = @"D:\c_Sharp_video\c_Sharp_video\tempVideo.mp4";
            string outputVideo = @"D:\c_Sharp_video\c_Sharp_video\When_Failure_Became_My_Teacher.mp4";
            

            if (!File.Exists(txtFile) || !File.Exists(imageFile))
            {
                Console.WriteLine("❌ Missing input files."); return;
            }

            string text = File.ReadAllText(txtFile);
            GenerateAudioAndSrt(text, mp3File, srtFile, 10);

            if (!File.Exists(mp3File) || !File.Exists(srtFile))
            {
                Console.WriteLine("❌ Failed to generate audio or subtitles."); return;
            }

            string scaledImage = PreScaleImage(imageFile, 1280, 720); // pleas create picture aspect ratio 21:9 to fit in video on Youtube

            double audioDuration = GetAudioDuration(mp3File);
            RunFFmpeg(
                $"-loop 1 -i \"{scaledImage}\" -i \"{mp3File}\" " +
                $"-c:v libx264 -preset fast -crf 23 -tune stillimage -c:a aac -b:a 192k " +
                $"-pix_fmt yuv420p -t {audioDuration} -movflags +faststart -y \"{tempVideo}\""
            );

            if (scaledImage != imageFile) File.Delete(scaledImage);
            if (!File.Exists(tempVideo)) { Console.WriteLine("❌ Failed to create video"); return; }

            string srtEscaped = srtFile.Replace("\\", "\\\\").Replace(":", "\\:");
            RunFFmpeg($"-i \"{tempVideo}\" -vf \"subtitles='{srtEscaped}':force_style='FontName=Arial,FontSize=24,PrimaryColour=&H00FFFFFF,OutlineColour=&H00000000,BorderStyle=1,Outline=2,Shadow=0,Alignment=2,MarginL=10,MarginR=10,MarginV=30'\" -c:v libx264 -preset fast -crf 23 -c:a copy -movflags +faststart -y \"{outputVideo}\"");

            File.Delete(tempVideo);

            if (File.Exists(outputVideo))
                Console.WriteLine($"\n the video created successful!: {outputVideo}");
            else
                Console.WriteLine("\n❌ Failed to create final video.");
        }

        static double GetAudioDuration(string audioFile)
        {
            try
            {
                using (var reader = new AudioFileReader(audioFile))
                {
                    return reader.TotalTime.TotalSeconds;
                }
            }
            catch
            {
                // fallback: estimate duration if reading fails
                var fileInfo = new FileInfo(audioFile);
                return fileInfo.Length / 16000.0; // rough estimate in seconds
            }
        }


        static void GenerateAudioAndSrt(string text, string mp3, string srt, int wordsPerBlock)
        {
            StringBuilder sb = new StringBuilder();
            TimeSpan currentTime = TimeSpan.Zero;
            int lineNum = 1;
            string[] sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var blockWavs = new System.Collections.Generic.List<string>();

            foreach (var sentence in sentences)
            {
                string[] words = sentence.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < words.Length; i += wordsPerBlock)
                {
                    int end = Math.Min(i + wordsPerBlock, words.Length);
                    string blockText = string.Join(" ", words, i, end - i);
                    string wavFile = Path.Combine(Path.GetTempPath(), $"block_{i}_{Guid.NewGuid():N}.wav");
                    using (var synth = new SpeechSynthesizer())
                    {
                        synth.SelectVoice("Microsoft Mark"); // or any neural voice you have , we can change any voice here 
                        synth.Rate = -4;
                        synth.Volume = 100;
                        synth.SetOutputToWaveFile(wavFile);
                        synth.Speak(blockText);
                    }


                    TimeSpan duration;
                    using (var reader = new AudioFileReader(wavFile)) duration = reader.TotalTime;

                    sb.AppendLine(lineNum.ToString());
                    sb.AppendLine($"{FormatTime(currentTime)} --> {FormatTime(currentTime + duration)}");
                    sb.AppendLine(blockText); sb.AppendLine();

                    currentTime += duration; lineNum++; blockWavs.Add(wavFile);
                }
            }

            string fileList = Path.Combine(Path.GetTempPath(), "file_list.txt");
            using (var w = new StreamWriter(fileList, false, new UTF8Encoding(false))) foreach (var f in blockWavs) w.WriteLine($"file '{f}'");

            RunFFmpeg($"-f concat -safe 0 -i \"{fileList}\" -c:a libmp3lame -q:a 4 -y \"{mp3}\"");

            File.WriteAllText(srt, sb.ToString());
            foreach (var f in blockWavs) File.Delete(f);
            File.Delete(fileList);
        }

        static string PreScaleImage(string img, int w, int h)
        {
            string scaled = Path.Combine(Path.GetTempPath(), $"scaled_{Guid.NewGuid():N}.png");

            RunFFmpeg(
                $"-i \"{img}\" -vf \"scale={w}:{h}:force_original_aspect_ratio=increase," +
                $"crop={w}:{h},format=yuv420p\" -y \"{scaled}\""
            );


            return scaled;
        }


        static string FormatTime(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";

        static void RunFFmpeg(string args)
        {
            var p = new Process { StartInfo = new ProcessStartInfo { FileName = ffmpegPath, Arguments = args, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8 } };
            p.Start(); string err = p.StandardError.ReadToEnd(); string outp = p.StandardOutput.ReadToEnd(); p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception($"FFmpeg failed: {err}");
        }
    }
}
