using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace QuickScreenshot
{
    internal sealed class PaddleOcrResult
    {
        public bool Success;
        public string Text;
        public string Error;
        public int LineCount;
        public int CharCount;
    }

    internal static class PaddleOcr
    {
        private const string PaddleVersion = "6.2.0";
        private const string RuntimeVersion = "3.3.0.1";
        private const string JsonVersion = "13.0.3";
        private const long TotalDownloadBytes = 171371789L;

        private static readonly object Sync = new object();
        private static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickScreenshot",
            "PaddleOCR",
            PaddleVersion);
        private static readonly string InstallDir = Root;
        private static readonly string MarkerPath = Path.Combine(Root, ".installed");

        private static object _engine;
        private static MethodInfo _detectMethod;
        private static bool _resolverInstalled;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        public static bool IsInstalled()
        {
            try
            {
                return File.Exists(MarkerPath) &&
                    File.Exists(Path.Combine(InstallDir, "PaddleOCRSharp.dll")) &&
                    File.Exists(Path.Combine(InstallDir, "Newtonsoft.Json.dll")) &&
                    File.Exists(Path.Combine(InstallDir, "PaddleOCR.dll")) &&
                    File.Exists(Path.Combine(InstallDir, "paddle_inference.dll")) &&
                    File.Exists(Path.Combine(InstallDir, "inference", "ppocr_keys.txt")) &&
                    File.Exists(Path.Combine(InstallDir, "inference",
                        "PP-OCRv6_small_det_infer", "inference.pdiparams")) &&
                    File.Exists(Path.Combine(InstallDir, "inference",
                        "PP-OCRv6_small_rec_infer", "inference.pdiparams"));
            }
            catch
            {
                return false;
            }
        }

        public static void Install(Action<int, string> progress, CancellationToken token)
        {
            string temp = Path.Combine(Path.GetTempPath(),
                "QuickScreenshot-paddle-" + Guid.NewGuid().ToString("N"));
            string stage = InstallDir + ".stage";
            try
            {
                Directory.CreateDirectory(temp);
                TryDeleteDirectory(stage);
                Directory.CreateDirectory(stage);

                string paddlePackage = Path.Combine(temp, "paddle.nupkg");
                string runtimePackage = Path.Combine(temp, "runtime.nupkg");
                string jsonPackage = Path.Combine(temp, "json.nupkg");

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(40);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("QuickScreenshot/1.1");
                    long received = 0;
                    Report(progress, 1, "正在下载 PaddleOCR 组件（约 170 MB）...");
                    Download(client, "https://api.nuget.org/v3-flatcontainer/paddleocrsharp/" +
                        PaddleVersion + "/paddleocrsharp." + PaddleVersion + ".nupkg",
                        paddlePackage, TotalDownloadBytes, ref received, progress, token);
                    Download(client, "https://api.nuget.org/v3-flatcontainer/paddle.runtime.win_x64/" +
                        RuntimeVersion + "/paddle.runtime.win_x64." + RuntimeVersion + ".nupkg",
                        runtimePackage, TotalDownloadBytes, ref received, progress, token);
                    Download(client, "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/" +
                        JsonVersion + "/newtonsoft.json." + JsonVersion + ".nupkg",
                        jsonPackage, TotalDownloadBytes, ref received, progress, token);
                }

                token.ThrowIfCancellationRequested();
                Report(progress, 76, "正在解压 OCR 组件...");
                string paddleDir = Path.Combine(temp, "paddle");
                string runtimeDir = Path.Combine(temp, "runtime");
                string jsonDir = Path.Combine(temp, "json");
                ZipFile.ExtractToDirectory(paddlePackage, paddleDir);
                ZipFile.ExtractToDirectory(runtimePackage, runtimeDir);
                ZipFile.ExtractToDirectory(jsonPackage, jsonDir);

                string ytLib = Path.Combine(paddleDir, "build", "ytLib");
                string inference = Path.Combine(ytLib, "inference");
                CopyFile(Path.Combine(paddleDir, "lib", "net48", "PaddleOCRSharp.dll"),
                    Path.Combine(stage, "PaddleOCRSharp.dll"));
                CopyFile(Path.Combine(jsonDir, "lib", "net45", "Newtonsoft.Json.dll"),
                    Path.Combine(stage, "Newtonsoft.Json.dll"));
                CopyFile(Path.Combine(ytLib, "PaddleOCR.dll"),
                    Path.Combine(stage, "PaddleOCR.dll"));
                CopyFile(Path.Combine(paddleDir, "LICENSE"),
                    Path.Combine(stage, "PADDLEOCR-LICENSE.txt"));

                string nativeDir = Path.Combine(runtimeDir, "build", "win_x64");
                foreach (string dll in Directory.GetFiles(nativeDir, "*.dll"))
                {
                    CopyFile(dll, Path.Combine(stage, Path.GetFileName(dll)));
                }

                CopyDirectory(Path.Combine(inference, "PP-OCRv6_small_det_infer"),
                    Path.Combine(stage, "inference", "PP-OCRv6_small_det_infer"));
                CopyDirectory(Path.Combine(inference, "PP-OCRv6_small_rec_infer"),
                    Path.Combine(stage, "inference", "PP-OCRv6_small_rec_infer"));
                CopyFile(Path.Combine(inference, "ppocr_keys.txt"),
                    Path.Combine(stage, "inference", "ppocr_keys.txt"));
                CopyFile(Path.Combine(inference, "en_dict.txt"),
                    Path.Combine(stage, "inference", "en_dict.txt"));
                CopyFile(Path.Combine(inference, "PaddleOCR.config.json"),
                    Path.Combine(stage, "inference", "PaddleOCR.config.json"));
                CopyFile(Path.Combine(inference, "PaddleOCRStructure.config.json"),
                    Path.Combine(stage, "inference", "PaddleOCRStructure.config.json"));

                token.ThrowIfCancellationRequested();
                Report(progress, 92, "正在完成安装...");
                File.WriteAllText(Path.Combine(stage, ".installed"), "PaddleOCR " + PaddleVersion);
                if (Directory.Exists(InstallDir))
                {
                    Directory.Delete(InstallDir, true);
                }

                Directory.Move(stage, InstallDir);
                Report(progress, 100, "增强 OCR 安装完成");
            }
            catch
            {
                TryDeleteDirectory(stage);
                throw;
            }
            finally
            {
                TryDeleteDirectory(temp);
            }
        }

        private static void Download(HttpClient client, string url, string path,
            long totalBytes, ref long received, Action<int, string> progress,
            CancellationToken token)
        {
            using (HttpResponseMessage response = client.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, token).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                long length = response.Content.Headers.ContentLength.HasValue
                    ? response.Content.Headers.ContentLength.Value
                    : 0;
                using (Stream input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (FileStream output = new FileStream(
                    path, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                {
                    byte[] buffer = new byte[65536];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        received += read;
                        int percent = (int)Math.Min(75,
                            received * 75 / Math.Max(1, totalBytes));
                        Report(progress, percent,
                            "正在下载 PaddleOCR 组件... " +
                            (received / 1024 / 1024) + " MB");
                    }
                }
            }
        }

        internal static OcrOutcome ToOutcome(PaddleOcrResult result)
        {
            OcrOutcome outcome = new OcrOutcome();
            if (result == null || !result.Success)
            {
                outcome.Error = result == null
                    ? "PaddleOCR 没有返回结果。"
                    : (string.IsNullOrEmpty(result.Error)
                        ? "PaddleOCR 没有识别到文字。"
                        : result.Error);
                outcome.LanguageName = "PaddleOCR";
                return outcome;
            }

            outcome.Success = true;
            outcome.Text = result.Text;
            outcome.LineCount = result.LineCount;
            outcome.CharCount = result.CharCount;
            outcome.LanguageName = "PaddleOCR（本地增强）";
            outcome.SuggestEnglishOcr = false;
            return outcome;
        }

        public static PaddleOcrResult Recognize(Bitmap bitmap)
        {
            PaddleOcrResult result = new PaddleOcrResult();
            if (bitmap == null || !IsInstalled())
            {
                result.Error = "增强 OCR 未安装。";
                return result;
            }

            string imagePath = Path.Combine(Path.GetTempPath(),
                "QuickScreenshot-ocr-" + Guid.NewGuid().ToString("N") + ".png");
            lock (Sync)
            {
                string previousDirectory = Environment.CurrentDirectory;
                try
                {
                    SetDllDirectory(InstallDir);
                    Environment.CurrentDirectory = InstallDir;
                    EnsureResolver();
                    if (_engine == null)
                    {
                        Assembly assembly = Assembly.LoadFrom(
                            Path.Combine(InstallDir, "PaddleOCRSharp.dll"));
                        Type type = assembly.GetType(
                            "PaddleOCRSharp.PaddleOCREngine", true);
                        _detectMethod = type.GetMethod("DetectText",
                            new Type[] { typeof(string) });
                        _engine = Activator.CreateInstance(type);
                    }

                    bitmap.Save(imagePath, ImageFormat.Png);
                    object ocrResult = _detectMethod.Invoke(
                        _engine, new object[] { imagePath });
                    if (ocrResult == null)
                    {
                        result.Error = "增强 OCR 没有返回结果。";
                        return result;
                    }

                    PropertyInfo textProperty = ocrResult.GetType().GetProperty("Text");
                    string text = textProperty == null
                        ? string.Empty
                        : (textProperty.GetValue(ocrResult, null) as string ?? string.Empty);
                    text = NormalizeText(text);
                    if (text.Length == 0)
                    {
                        result.Error = "增强 OCR 没有识别到文字。";
                        return result;
                    }

                    result.Success = true;
                    result.Text = text;
                    result.LineCount = text.Split('\n').Length;
                    result.CharCount = text.Replace("\r", "").Replace("\n", "").Length;
                    return result;
                }
                catch (Exception ex)
                {
                    AppLog.WriteException("PaddleOCR", ex);
                    result.Error = "增强 OCR 失败：" + ex.Message;
                    return result;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(imagePath))
                        {
                            File.Delete(imagePath);
                        }
                    }
                    catch
                    {
                    }

                    Environment.CurrentDirectory = previousDirectory;
                    SetDllDirectory(null);
                }
            }
        }

        private static void EnsureResolver()
        {
            if (_resolverInstalled)
            {
                return;
            }

            _resolverInstalled = true;
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                try
                {
                    string name = new AssemblyName(args.Name).Name + ".dll";
                    string path = Path.Combine(InstallDir, name);
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                }
                catch
                {
                    return null;
                }
            };
        }

        private static string NormalizeText(string text)
        {
            text = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"(?<=[\u4e00-\u9fff])(?=[A-Za-z0-9])", " ");
            text = Regex.Replace(text, @"(?<=[A-Za-z0-9])(?=[\u4e00-\u9fff])", " ");
            text = Regex.Replace(text,
                @"\)(xlsx|xls|docx|doc|pptx|ppt|pdf|txt|png|jpg|jpeg|gif|zip|rar|7z)\b",
                ").$1", RegexOptions.IgnoreCase);
            return text.Trim();
        }

        private static void CopyFile(string source, string destination)
        {
            string dir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(source, destination, true);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            }

            foreach (string dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
            }
        }

        private static void Report(Action<int, string> progress, int percent, string text)
        {
            if (progress != null)
            {
                progress(percent, text);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }
}
