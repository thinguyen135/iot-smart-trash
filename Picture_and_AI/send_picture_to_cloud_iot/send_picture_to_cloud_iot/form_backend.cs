using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace send_picture_to_cloud_iot
{
    public partial class form_backend : Form
    {
        private static readonly string[] FirebaseRtdbUrls =
        {
            "https://iot-smart-trash-212d9-default-rtdb.firebaseio.com",
            "https://iot-smart-trash-212d9-default-rtdb.asia-southeast1.firebasedatabase.app"
        };
        private static readonly string[] FirebaseStorageBuckets =
        {
            "iot-smart-trash-212d9.firebasestorage.app",
            "iot-smart-trash-212d9.appspot.com"
        };
        private const string FirebaseApiKey = "AIzaSyBkU6zS_GhZ-ziCHGKec5XlbNF1SC8PkVQ";
        private const string GeminiApiKey = "";
        private const int GeminiMaxRetries = 4;

        private readonly HttpClient httpClient = new HttpClient();
        private readonly JavaScriptSerializer jsonSerializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly Timer firebaseTimer = new Timer();
        private bool isProcessing;
        private string activeFirebaseRtdbUrl = FirebaseRtdbUrls[0];
        private string selectedImagePath = "";

        public form_backend()
        {
            InitializeComponent();

            firebaseTimer.Interval = 1500;
            firebaseTimer.Tick += async (sender, args) => await CheckObstacleSignal();
            firebaseTimer.Start();
        }

        private async Task CheckObstacleSignal()
        {
            if (isProcessing) return;

            try
            {
                string response = await GetJsonFromFirebase("/sensors/pir/obstacle.json");
                lblStatus.Text = "Firebase đã kết nối";

                if (response != null && response.Trim() == "1")
                {
                    await TriggerSelectionAndClassification();
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Chưa kết nối RTDB, vẫn có thể chọn ảnh thủ công.";
                Console.WriteLine("Lỗi Firebase: " + ex.Message);
            }
        }

        private async Task TriggerSelectionAndClassification()
        {
            MessageBox.Show("Phát hiện rác mới. Hãy chọn ảnh để AI phân loại.");
            bool selected = await PickImageAndClassify();
            if (!selected)
            {
                await PutJson("/sensors/pir/obstacle.json", 0);
                lblStatus.Text = "Đã hủy chọn ảnh, obstacle đã reset về 0.";
            }
        }

        private async void btnUpload_Click(object sender, EventArgs e)
        {
            await PickImageAndClassify();
        }

        private async Task<bool> PickImageAndClassify()
        {
            if (isProcessing) return false;
            isProcessing = true;

            try
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = "Image Files|*.jpg;*.jpeg;*.png";
                    if (dialog.ShowDialog() != DialogResult.OK) return false;

                    selectedImagePath = dialog.FileName;
                    pictureBox1.ImageLocation = selectedImagePath;
                }

                await ProcessSelectedImage(selectedImagePath);
                return true;
            }
            finally
            {
                if (btnUpload.Enabled)
                {
                    isProcessing = false;
                }
            }
        }

        private async Task ProcessSelectedImage(string imagePath)
        {
            isProcessing = true;
            btnUpload.Enabled = false;

            try
            {
                lblStatus.Text = "Đang upload ảnh lên Firebase Storage...";
                string storagePath = "trash-images/current_trash.jpg";
                string imageUrl;

                try
                {
                    imageUrl = await UploadImageToFirebaseStorage(imagePath, storagePath);
                }
                catch (Exception storageException)
                {
                    storagePath = "";
                    imageUrl = CreateRealtimePreviewDataUrl(imagePath);
                    lblStatus.Text = "Storage chưa dùng được, tạm gửi ảnh preview qua Realtime Database...";
                    Console.WriteLine(storageException.Message);
                }

                try
                {
                    await SendPreviewToFirebase(imageUrl, storagePath);
                    lblStatus.Text = "Đã gửi ảnh lên web, đang phân loại bằng Gemini...";
                }
                catch (Exception previewException)
                {
                    lblStatus.Text = "Không gửi được ảnh preview, vẫn tiếp tục phân loại...";
                    Console.WriteLine(previewException.Message);
                }

                lblStatus.Text = "Đang phân loại bằng Gemini...";
                string aiResult = NormalizeCategory(await ClassifyTrashWithGemini(imagePath));

                if (aiResult == "")
                {
                    lblStatus.Text = "AI không trả về H, N hoặc G";
                    return;
                }

                string label = GetTrashLabel(aiResult);
                lblResult.Text = $"Kết quả AI: {aiResult} - {label}";

                lblStatus.Text = "Đang ghi kết quả lên Realtime Database...";
                await SendResultToFirebase(aiResult, label, imageUrl, storagePath);

                lblStatus.Text = "Hoàn tất. Web dashboard sẽ cập nhật realtime.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Lỗi xử lý: " + ex.Message;
                MessageBox.Show("Lỗi xử lý ảnh: " + ex.Message);
            }
            finally
            {
                btnUpload.Enabled = true;
                isProcessing = false;
            }
        }

        private async Task<string> UploadImageToFirebaseStorage(string filePath, string storagePath)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            string escapedPath = Uri.EscapeDataString(storagePath);
            string lastError = "";

            foreach (string bucket in FirebaseStorageBuckets)
            {
                string uploadUrl = $"https://firebasestorage.googleapis.com/v0/b/{bucket}/o?uploadType=media&name={escapedPath}&key={FirebaseApiKey}";

                using (ByteArrayContent content = new ByteArrayContent(fileBytes))
                {
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GetMimeType(filePath));
                    HttpResponseMessage response = await httpClient.PostAsync(uploadUrl, content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        long cacheBreaker = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        return $"https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{escapedPath}?alt=media&t={cacheBreaker}";
                    }

                    lastError = $"Bucket {bucket}: {responseBody}";
                }
            }

            throw new InvalidOperationException(
                "Firebase Storage upload failed. Hãy kiểm tra Firebase Console > Storage đã được tạo chưa, " +
                "và Rules của Storage không phải Rules của Realtime Database. Chi tiết: " + lastError);
        }

        private static string CreateRealtimePreviewDataUrl(string filePath)
        {
            const int maxSide = 900;

            using (Image source = Image.FromFile(filePath))
            {
                double scale = Math.Min(1.0, (double)maxSide / Math.Max(source.Width, source.Height));
                int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                int height = Math.Max(1, (int)Math.Round(source.Height * scale));

                using (Bitmap preview = new Bitmap(width, height))
                using (Graphics graphics = Graphics.FromImage(preview))
                using (MemoryStream stream = new MemoryStream())
                {
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.DrawImage(source, 0, 0, width, height);

                    ImageCodecInfo jpegEncoder = GetImageEncoder(ImageFormat.Jpeg);
                    if (jpegEncoder != null)
                    {
                        using (EncoderParameters parameters = new EncoderParameters(1))
                        {
                            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 72L);
                            preview.Save(stream, jpegEncoder, parameters);
                        }
                    }
                    else
                    {
                        preview.Save(stream, ImageFormat.Jpeg);
                    }

                    return "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
                }
            }
        }

        private static ImageCodecInfo GetImageEncoder(ImageFormat format)
        {
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageDecoders())
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        private async Task SendResultToFirebase(string category, string label, string imageUrl, string storagePath)
        {
            var currentEvent = new
            {
                category,
                label,
                imageUrl,
                storagePath,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await PutJson("/trash_type/category.json", category);
            await PutJson("/trash_events/current.json", currentEvent);
            await PutJson($"/trash_events/history/{currentEvent.createdAt}.json", currentEvent);
        }

        private async Task SendPreviewToFirebase(string imageUrl, string storagePath)
        {
            var currentEvent = new
            {
                category = "",
                label = "Đang phân loại...",
                imageUrl,
                storagePath,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await PutJson("/trash_events/current.json", currentEvent);
        }

        private async Task PutJson(string path, object value)
        {
            string json = jsonSerializer.Serialize(value);
            using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
            {
                HttpResponseMessage response = await httpClient.PutAsync(activeFirebaseRtdbUrl + path, content);
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("Firebase RTDB write failed: " + responseBody);
                }
            }
        }

        private async Task<string> GetJsonFromFirebase(string path)
        {
            string lastError = "";

            foreach (string url in FirebaseRtdbUrls)
            {
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(url + path);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        activeFirebaseRtdbUrl = url;
                        return responseBody;
                    }

                    lastError = $"{url}: {response.StatusCode} {responseBody}";
                }
                catch (Exception ex)
                {
                    lastError = $"{url}: {ex.Message}";
                }
            }

            throw new InvalidOperationException(lastError);
        }

        private async Task<string> ClassifyTrashWithGemini(string filePath)
        {
            byte[] imageBytes = File.ReadAllBytes(filePath);
            string base64Image = Convert.ToBase64String(imageBytes);
            string mimeType = GetMimeType(filePath);
            string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={GeminiApiKey}";
            string prompt = "Bạn là hệ thống AI phân loại rác. Dựa vào ảnh, hãy trả về đúng 1 ký tự duy nhất: 'H' nếu là rác hữu cơ như thức ăn, rau củ; 'N' nếu là rác nhựa, kim loại hoặc vô cơ; 'G' nếu là giấy hoặc carton. Không giải thích gì thêm.";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new { inline_data = new { mime_type = mimeType, data = base64Image } }
                        }
                    }
                }
            };

            string jsonContent = jsonSerializer.Serialize(requestBody);

            for (int attempt = 1; attempt <= GeminiMaxRetries; attempt++)
            {
                using (StringContent httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage response = await httpClient.PostAsync(apiUrl, httpContent);
                    string responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        Dictionary<string, object> jsonResponse = jsonSerializer.DeserializeObject(responseString) as Dictionary<string, object>;
                        return ExtractGeminiText(jsonResponse);
                    }

                    if (IsRetryableGeminiError(response.StatusCode) && attempt < GeminiMaxRetries)
                    {
                        int waitSeconds = attempt * 3;
                        lblStatus.Text = $"Gemini đang quá tải, thử lại {attempt + 1}/{GeminiMaxRetries} sau {waitSeconds}s...";
                        await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
                        continue;
                    }

                    throw new InvalidOperationException("Gemini error: " + responseString);
                }
            }

            throw new InvalidOperationException("Gemini đang quá tải. Vui lòng thử lại sau vài phút.");
        }

        private static bool IsRetryableGeminiError(HttpStatusCode statusCode)
        {
            return (int)statusCode == 429 ||
                   statusCode == HttpStatusCode.InternalServerError ||
                   statusCode == HttpStatusCode.BadGateway ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == HttpStatusCode.GatewayTimeout;
        }

        private static string ExtractGeminiText(Dictionary<string, object> jsonResponse)
        {
            if (jsonResponse == null || !jsonResponse.ContainsKey("candidates")) return "";

            object[] candidates = jsonResponse["candidates"] as object[];
            if (candidates == null || candidates.Length == 0) return "";

            Dictionary<string, object> candidate = candidates[0] as Dictionary<string, object>;
            if (candidate == null || !candidate.ContainsKey("content")) return "";

            Dictionary<string, object> content = candidate["content"] as Dictionary<string, object>;
            if (content == null || !content.ContainsKey("parts")) return "";

            object[] parts = content["parts"] as object[];
            if (parts == null || parts.Length == 0) return "";

            Dictionary<string, object> firstPart = parts[0] as Dictionary<string, object>;
            if (firstPart == null || !firstPart.ContainsKey("text")) return "";

            return firstPart["text"]?.ToString() ?? "";
        }

        private static string NormalizeCategory(string rawResult)
        {
            string text = (rawResult ?? "").Trim().ToUpperInvariant();
            foreach (char value in text)
            {
                if (value == 'H' || value == 'N' || value == 'G')
                {
                    return value.ToString();
                }
            }
            return "";
        }

        private static string GetTrashLabel(string category)
        {
            switch (category)
            {
                case "H":
                    return "Hữu cơ";
                case "N":
                    return "Nhựa / vô cơ";
                case "G":
                    return "Giấy / carton";
                default:
                    return "Không xác định";
            }
        }

        private static string GetMimeType(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".png") return "image/png";
            if (extension == ".jpg" || extension == ".jpeg") return "image/jpeg";
            return "application/octet-stream";
        }
    }
}
