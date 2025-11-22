using System.Diagnostics;
using System.Management;
using System.Net;
using System.Text;
using System.Text.Json;
using Timer = System.Windows.Forms.Timer;

namespace SignalBringe
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        HttpListener listener = new HttpListener();
        List<HeartbeatMessage> heartbeats = new List<HeartbeatMessage>();
        DateTime RobotTime = DateTime.Now;

        private void button1_Click(object sender, EventArgs e)
        {
            textBox1.Enabled = false;
            textBox2.Enabled = false;
            label2.Text = "Listening!!";
            SaveToJsonFile("log.txt");
            StartListener();

            Timer timer = new Timer();
            timer.Interval = 20000;
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void SaveToJsonFile(string filePath)
        {
            try
            {
                var data = new
                {
                    Text1 = textBox1.Text,
                    Text2 = textBox2.Text
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Xatolik: {ex.Message}");
            }
        }

        private void ReadFromJsonFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    this.textBox1.Text = ""; this.textBox2.Text = "";
                }
                else
                {
                    string json = File.ReadAllText(filePath);
                    var data = JsonSerializer.Deserialize<JsonData>(json);

                    textBox1.Text = data.Text1; textBox2.Text = data.Text2;

                    textBox1.Enabled = false;
                    textBox2.Enabled = false;
                    label2.Text = "Listening!!";
                    SaveToJsonFile("log.txt");
                    StartListener();

                    Timer timer = new Timer();
                    timer.Interval = 20000;
                    timer.Tick += Timer_Tick;
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Xatolik: {ex.Message}");
            }
        }

        private class JsonData
        {
            public string Text1 { get; set; }
            public string Text2 { get; set; }
        }

        private double GetCpuUsageAccurate()
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = GetTotalCpuTime();

            Thread.Sleep(1000);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = GetTotalCpuTime();

            double cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            double totalMsPassed = (endTime - startTime).TotalMilliseconds * Environment.ProcessorCount;

            double cpuUsageTotal = (cpuUsedMs / totalMsPassed) * 100;
            return Math.Round(cpuUsageTotal, 1);
        }

        private TimeSpan GetTotalCpuTime()
        {
            TimeSpan total = TimeSpan.Zero;
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    total += proc.TotalProcessorTime;
                }
                catch { }
            }
            return total;
        }

        private (double totalRam, double usedRam, double usedPercent) GetRamUsage()
        {
            double totalRam = 0;
            double freeRam = 0;

            using (var searcher = new ManagementObjectSearcher("select TotalVisibleMemorySize, FreePhysicalMemory from Win32_OperatingSystem"))
            {
                foreach (var obj in searcher.Get())
                {
                    totalRam = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / (1024 * 1024);
                    freeRam = Convert.ToDouble(obj["FreePhysicalMemory"]) / (1024 * 1024);
                }
            }

            double usedRam = totalRam - freeRam;
            double usedPercent = (usedRam / totalRam) * 100;

            return (totalRam, usedRam, usedPercent);
        }

        private double GetCurrentCpuSpeedGHz()
        {
            double speedMHz = 0;
            using (var searcher = new ManagementObjectSearcher("select CurrentClockSpeed from Win32_Processor"))
            {
                foreach (var item in searcher.Get())
                {
                    speedMHz = Convert.ToDouble(item["CurrentClockSpeed"]);
                    break;
                }
            }
            return speedMHz / 1000.0;
        }

        private async void StartListener()
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:5000/");
            listener.Start();

            while (listener.IsListening)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();

                    _ = Task.Run(() => HandleRequest(ctx));
                }
                catch (HttpListenerException ex)
                {
                    MessageBox.Show("Listener to‘xtadi: " + ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Xatolik: " + ex.Message);
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;

                if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/heartbeat")
                {
                    using var sr = new StreamReader(req.InputStream, req.ContentEncoding);
                    string body = sr.ReadToEnd().TrimEnd('\0');

                    var heartbeatMessage = Parse(body);
                    if (heartbeatMessage == null)
                        return;

                    var existing = heartbeats.FirstOrDefault(h => h.From == heartbeatMessage.From);

                    if (existing != null)
                    {
                        existing.Balance = heartbeatMessage.Balance;
                        existing.Equity = heartbeatMessage.Equity;

                        if (existing.From == "Robot")
                        {
                            RobotTime = DateTime.Now;
                        }
                    }
                    else
                    {
                        heartbeats.Add(heartbeatMessage);
                    }

                    byte[] resp = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.OutputStream.Write(resp, 0, resp.Length);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                MessageBox.Show("Xatolik: " + ex.Message);
            }
            finally
            {
                ctx.Response.Close();
            }
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            var data = await Task.Run(() =>
            {
                double cpuLoad = GetCpuUsageAccurate();
                double cpuGHz = GetCurrentCpuSpeedGHz();
                var (totalRam, usedRam, usedPercent) = GetRamUsage();
                return (cpuLoad, cpuGHz, totalRam, usedRam, usedPercent);
            });

            var robot = heartbeats.FirstOrDefault(h => h.From == "Robot");

            if (robot != null)
            {
                string robotMsg = robot.Message;

                if ((DateTime.Now - RobotTime).TotalSeconds > 50)
                    robotMsg = "Bog'lanish yo'q!";

                string message = BuildStatusMessage(robotMsg);

                Client newClient = new Client
                {
                    VpsId = textBox1.Text,
                    ClientLogin = textBox2.Text,
                    AccountBalance = robot.Balance,
                    AccountEquity = robot.Equity,
                    RobotStatus = (DateTime.Now - RobotTime).TotalSeconds <= 50,
                    ServerRam = $"{data.usedPercent:F1}",
                    ServerCpu = $"{data.cpuLoad:F1}",
                    ProblemDescription = message
                };

                await SendClientDataAsync(newClient);
            }
            else
            {
                string message = BuildStatusMessage("Bog'lanish yo'q!");

                Client newClient = new Client
                {
                    VpsId = textBox1.Text,
                    ClientLogin = textBox2.Text,
                    AccountBalance = "0",
                    AccountEquity = "0",
                    RobotStatus = (DateTime.Now - RobotTime).TotalSeconds <= 50,
                    ServerRam = $"{data.usedPercent:F1}",
                    ServerCpu = $"{data.cpuLoad:F1}",
                    ProblemDescription = message
                };

                await SendClientDataAsync(newClient);
            }
        }

        public async Task SendClientDataAsync(Client newClient)
        {
            try
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    string url = "https://vps-analizer-7a5f56f72765.herokuapp.com/api/User/PutVPSSource";

                    string json = JsonSerializer.Serialize(newClient);

                    HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await httpClient.PutAsync(url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                    else if ((int)response.StatusCode == 500)
                    {
                        string errorText = await response.Content.ReadAsStringAsync();
                    }
                    else
                    {
                        string errorText = await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"So‘rov yuborishda xatolik:\n{ex.Message}", "Xatolik", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static string BuildStatusMessage(string robotMessage)
        {
            bool hasInvestor = !string.IsNullOrWhiteSpace(robotMessage);

            var sb = new StringBuilder();

            if (!hasInvestor)
            {
                sb.Append("Hammasi joyida!");
            }
            else if (hasInvestor)
            {
                sb.AppendLine($"Robot xabari: {robotMessage.Trim()}");
            }

            if (hasInvestor)
                sb.AppendLine($" Robot xabari: {robotMessage} ");

            return sb.ToString().TrimEnd();
        }

        public static HeartbeatMessage? Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                HeartbeatMessage msg = new HeartbeatMessage
                {
                    From = root.TryGetProperty("From", out var from) ? from.ToString() : "",
                    Balance = root.TryGetProperty("Balance", out var bal) ? bal.ToString() : "",
                    Equity = root.TryGetProperty("Equity", out var eq) ? eq.ToString() : "",
                    Login = root.TryGetProperty("Login", out var lgn) ? lgn.ToString() : "",
                    Message = root.TryGetProperty("Message", out var message) ? message.ToString() : ""
                };

                return msg;
            }
            catch (Exception ex)
            {
                Console.WriteLine("JSON parsing xatolik: " + ex.Message);
                return null;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            ReadFromJsonFile("log.txt");
        }
    }

    public class HeartbeatMessage
    {
        public string From { get; set; }
        public string Login { get; set; }
        public string Balance { get; set; }
        public string Equity { get; set; }
        public string Message { get; set; }
    }

    public class Client
    {
        public string VpsId { get; set; }
        public string ClientLogin { get; set; }
        public string AccountBalance { get; set; }
        public string AccountEquity { get; set; }
        public bool RobotStatus { get; set; }
        public string ServerRam { get; set; }
        public string ServerCpu { get; set; }
        public string ProblemDescription { get; set; }
    }
}
