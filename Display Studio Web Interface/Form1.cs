using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using QRCoder;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Net.Sockets;


namespace Display_Studio_Web_Interface
{
    public partial class Form1 : Form
    {
        private HttpListener _httpListener;
        private Thread _listenerThread;
        private WorkspaceTree _currentTree;
        private HttpClient HttpClient;
        private string displayStudioIP = "localhost";
        private WorkspaceTreesRoot _workspaceRoot;
        private PictureBox qrPictureBox;


        public Form1()
        {
            InitializeComponent();
            this.FormClosing += Form1_FormClosing;

        }
        private async void Form1_Load(object sender, EventArgs e)
        {
            _workspaceRoot = await LoadAllWorkspaces("http://" + displayStudioIP + ":28052/daktronics/displaystudio/Workspace/1.0/WorkspaceTrees");
            if (_workspaceRoot != null)
            {
                _currentTree = _workspaceRoot.Workspaces[0]; // default to first

                DisplayButtons(_currentTree);
            }
            StartWebServer();
        }
        private async Task<WorkspaceTreesRoot> LoadAllWorkspaces(string url)
        {
            try
            {
                UpdateStatus("Connecting To Display Studio. This will take a few minutes");
                using (HttpClient client = new HttpClient())
                {
                    var response = await client.GetStringAsync(url);
                    UpdateStatus("Buttons received");

                    var serializer = new XmlSerializer(typeof(WorkspaceTreesRoot));
                    using (var reader = new StringReader(response))
                    {
                        return (WorkspaceTreesRoot)serializer.Deserialize(reader);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Error: " + ex.Message);
                return null;
            }
        }
        private void DisplayButtons(WorkspaceTree tree)
        {
            var buttons = tree.Pages?[0].Containers?[0].Tabs?[0].Buttons;
            if (buttons == null) return;

            int cellWidth = 160;
            int cellHeight = 50;
            int margin = 10;

            foreach (var btn in buttons)
            {
                Button b = new Button();
                b.Text = btn.Name;
                b.Tag = btn.Id;
                b.Width = cellWidth;
                b.Height = cellHeight;

                // Position according to Column and Row
                int x = margin + (btn.Column * (cellWidth + margin));
                int y = margin + (btn.Row * (cellHeight + margin));

                b.Left = x;
                b.Top = y;

                // Proper ARGB parsing

                b.BackColor = ColorTranslator.FromHtml(btn.BackgroundColor);
                b.ForeColor = ColorTranslator.FromHtml(btn.ForegroundColor);

                b.Click += Button_Click;
                this.Controls.Add(b);
                b.Visible = false;
            }
        }
        private void Button_Click(object sender, EventArgs e)
        {
            var button = sender as Button;
            string id = button.Tag.ToString();

            TriggerButtonAsync(id);
        }
        private void StartWebServer()
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://+:80/"); // your web UI URL
            _httpListener.Start();

            _listenerThread = new Thread(() =>
            {
                while (_httpListener.IsListening)
                {
                    try
                    {
                        var context = _httpListener.GetContext();
                        HandleRequest(context);
                    }
                    catch (Exception ex)
                    {
                        // Handle exceptions or ignore when stopping
                    }
                }
            });
            _listenerThread.Start();
        }
        private void HandleRequest(HttpListenerContext context)
        {
            string responseString = "";
            var request = context.Request;
            var response = context.Response;

            try
            {
                if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/")
                {
                    int workspaceIndex = 0, pageIndex = 0, containerIndex = 0, tabIndex = 0;
                    int.TryParse(request.QueryString["workspace"], out workspaceIndex);
                    int.TryParse(request.QueryString["page"], out pageIndex);
                    int.TryParse(request.QueryString["container"], out containerIndex);
                    int.TryParse(request.QueryString["tab"], out tabIndex);


                    responseString = GenerateHtmlForButtons(_workspaceRoot, workspaceIndex, pageIndex, containerIndex, tabIndex);
                }
                else if (request.HttpMethod == "GET" && request.Url.AbsolutePath.StartsWith("/click"))
                {
                    var id = request.QueryString["id"];
                    TriggerButtonAsync(id);

                    string referer = request.Headers["Referer"];
                    if (!string.IsNullOrEmpty(referer))
                    {
                        // Redirect back to the referring page
                        response.StatusCode = 302;
                        response.RedirectLocation = referer;
                        response.Close(); // VERY IMPORTANT
                        return;
                    }
                    else
                    {
                        // Fallback if referer is not available
                        responseString = $"<html><body>Button {id} clicked!<br/><a href='/'>Back</a></body></html>";
                    }
                }
                else
                {
                    responseString = "<html><body>404 Not Found</body></html>";
                    response.StatusCode = 404;
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                response.ContentType = "text/html";
                using (var output = response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                // Optional: log or debug exception
                var error = $"<html><body>Error: {ex.Message}</body></html>";
                byte[] buffer = Encoding.UTF8.GetBytes(error);
                response.StatusCode = 500;
                response.ContentLength64 = buffer.Length;
                response.ContentType = "text/html";
                using (var output = response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
            }
        }
        public async Task TriggerButtonAsync(string id)
        {
            // FIX: assign the HttpClient
            HttpClient = CreateHttpClient();

            // Now use the properly initialized HttpClient
            var response = await HttpClient.PostAsync("ExecuteButton/" + id, new StreamContent(new MemoryStream()));
            response.EnsureSuccessStatusCode();
        }
        public HttpClient CreateHttpClient()
        {
            Uri uri = new Uri("http://" + displayStudioIP + ":28052/daktronics/displaystudio/Trigger/1.0/", UriKind.Absolute);
            Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            AssemblyName name = assembly.GetName();

            return new HttpClient(new HttpClientHandler())
            {
                BaseAddress = uri,
                DefaultRequestHeaders =
                {
                    UserAgent = { new ProductInfoHeaderValue(name.Name.Replace(' ', '_'), name.Version.ToString()) },
                    Accept = { new MediaTypeWithQualityHeaderValue("application/json") }
                }
            };
        }
        private string GenerateHtmlForButtons(WorkspaceTreesRoot root, int workspaceIndex = 0, int pageIndex = 0, int containerIndex = 0, int tabIndex = 0)
        {

            if (root?.Workspaces == null || workspaceIndex >= root.Workspaces.Count)
                return "<html><body>Invalid workspace</body></html>";

            var tree = root.Workspaces[workspaceIndex];

            var page = tree.Pages[pageIndex];
            if (page.Containers == null || containerIndex >= page.Containers.Count)
                return "<html><body>Invalid container</body></html>";

            var container = page.Containers[containerIndex];
            if (container.Tabs == null || tabIndex >= container.Tabs.Count)
                return "<html><body>Invalid tab</body></html>";

            var tab = container.Tabs[tabIndex];
            var buttons = tab.Buttons ?? new List<ButtonNode>();
            int maxCol = buttons.Count > 0 ? buttons.Max(b => b.Column) + 1 : 1;

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><title>Workspace</title>");
            sb.AppendLine("<style>");
            sb.AppendLine($@"
        body {{
            margin: 0;
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            display: flex;
            flex-direction: column;
            height: 100vh;
            background: #1b1b1b;
            color: white;
        }}

        a {{
            text-decoration: none;
            color: inherit;
        }}

        .top-bar, .bottom-bar {{
            display: flex;
            
            align-items: center;
            background-color: #2c2c2c;
            padding: 3px 5px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.5);
            position: sticky;
            z-index: 1000;
        }}

        .top-bar {{
            top: 0;
            gap: 10px;
        }}

        .bottom-bar {{
            bottom: 0;
            flex-wrap: wrap;
        }}

        .top-bar div, .bottom-bar {{
            display: flex;
            flex-wrap: wrap;
            gap: 10px;
        }}

        .top-bar a, .bottom-bar a {{
            padding: 6px 12px;
            background-color: #444;
            border-radius: 5px;
            transition: background-color 0.3s ease;
            font-size: 0.9rem;
        }}

        .top-bar a:hover, .bottom-bar a:hover {{
            background-color: #666;
        }}

        .top-bar a.active, .bottom-bar a.active {{
            background-color: #007acc;
            font-weight: bold;
        }}

        .grid-container {{
            display: grid;
            grid-template-columns: repeat({maxCol}, 190px);
            grid-auto-rows: 40px;
            gap: 10px;
            padding: 10px;
            overflow-y: auto;
            flex: 1;
            background: #1b1b1b;
        }}

        .grid-item {{
            display: flex;           
            align-items: center;
            border-radius: 1px;
            font-weight: bold;
            font-size: 0.74rem;
            border: 1px solid rgba(255,255,255,0.2);    
            transition: transform 0.1s ease-in-out;
            padding: 5px;

        }}

        .grid-item:hover {{
            transform: scale(1.05);
        }}
    ");
            sb.AppendLine("</style>");
            sb.AppendLine("</head><body>");

            // Top Navigation Bar

            // Workspace Navigation
            sb.AppendLine("<div class='top-bar'> Workspace");
            for (int i = 0; i < root.Workspaces.Count; i++)
            {
                string cls = (i == workspaceIndex) ? "active" : "";
                sb.Append($"<a class='{cls}' href='/?workspace={i}&page=0&container=0&tab=0'>");
                sb.Append(WebUtility.HtmlEncode(root.Workspaces[i].Name));
                sb.Append("</a>");
            }
            sb.AppendLine("</div>");

            // Pages Section
            sb.AppendLine("<div class='top-bar'> Page");
            for (int i = 0; i < tree.Pages.Count; i++)
            {
                string cls = (i == pageIndex) ? "active" : "";
                sb.Append($"<a class='{cls}' href='/?workspace={workspaceIndex}&page={i}&container=0&tab=0'>");
                sb.Append(WebUtility.HtmlEncode(tree.Pages[i].Name));
                sb.Append("</a>");
            }
            sb.AppendLine("</div>");

            // Containers Section
            sb.AppendLine("<div class='top-bar'> Scripting Container");
            for (int i = 0; i < page.Containers.Count; i++)
            {
                string cls = (i == containerIndex) ? "active" : "";
                sb.Append($"<a class='{cls}' href='/?workspace={workspaceIndex}&page={pageIndex}&container={i}&tab=0'>");
                sb.Append(WebUtility.HtmlEncode(page.Containers[i].Name));
                sb.Append("</a>");
            }
            sb.AppendLine("</div>");



            // Main Button Grid
            sb.AppendLine("<div class='grid-container'>");
            foreach (var btn in buttons)
            {
                var bgColor = btn.BackgroundColor ?? "#cccccc";
                var fgColor = btn.ForegroundColor ?? "#000000";
                var id = WebUtility.HtmlEncode(btn.Id);
                var name = WebUtility.HtmlEncode(btn.Name);
                int col = btn.Column + 1;
                int row = btn.Row + 1;

                sb.Append($"<a href='/click?id={id}' class='grid-item' style='");
                sb.Append($"grid-column: {col}; grid-row: {row}; ");
                sb.Append($"background-color:{bgColor}; color:{fgColor};'>");
                sb.Append(name);
                sb.Append("</a>");
            }
            sb.AppendLine("</div>"); // end .grid-container

            // Bottom Navigation Bar (Tabs)
            sb.AppendLine("<div class='bottom-bar'>");
            for (int i = 0; i < container.Tabs.Count; i++)
            {
                string cls = (i == tabIndex) ? "active" : "";
                sb.Append($"<a class='{cls}' href='/?workspace={workspaceIndex}&page={pageIndex}&container={containerIndex}&tab={i}'>");
                sb.Append(WebUtility.HtmlEncode(container.Tabs[i].Name));
                sb.Append("</a>");
            }
            sb.AppendLine("</div>"); // end .bottom-bar

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_httpListener != null && _httpListener.IsListening)
            {
                _httpListener.Stop();
                _httpListener.Close();
            }

            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                _listenerThread.Join();
            }
        }
        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => statusLabel.Text = "Status: " + message));
            }
            else
            {
                statusLabel.Text = "Status: " + message;
            }
        }
        private async void refreshButtoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Controls.Clear(); // Remove existing controls
            InitializeComponent(); // Re-add UI components (including reload button)

            _workspaceRoot = await LoadAllWorkspaces("http://" + displayStudioIP + ":28052/daktronics/displaystudio/Workspace/1.0/WorkspaceTrees");
            if (_workspaceRoot != null)
            {
                _currentTree = _workspaceRoot.Workspaces[0]; // default to first

                DisplayButtons(_currentTree);
            }
        }
        private void webInterfaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowQrCode("http://" + GetLocalIPAddress());
        }
        static string GetLocalIPAddress()
        {
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // Filter out network interfaces that are not up or not Ethernet or Wireless
                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    {
                        foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
                        {
                            // Get IPv4 addresses only, skipping IPv6
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error retrieving local IP address: " + ex.Message);
            }
            return null;
        }
        public static void ShowQrCode(string destination)
        {
            using (var form = new QrCodeForm(destination))
            {
                form.ShowDialog(); // Show as a modal dialog
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Windows.Forms.Application.Exit();

        }
    }


    public class QrCodeForm : Form
    {
        private PictureBox qrPictureBox;

        public QrCodeForm(string destination)
        {
            this.Text = "Scan QR code for web interface";
            this.Size = new Size(400, 400);
            this.StartPosition = FormStartPosition.CenterScreen;

            qrPictureBox = new PictureBox();
            qrPictureBox.Dock = DockStyle.Fill;
            qrPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            this.Controls.Add(qrPictureBox);

            GenerateAndShowQr(destination);
        }

        private void GenerateAndShowQr(string destination)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            {
                using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(destination, QRCodeGenerator.ECCLevel.Q))
                {
                    using (QRCode qrCode = new QRCode(qrCodeData))
                    {
                        Bitmap qrCodeImage = qrCode.GetGraphic(20);
                        qrPictureBox.Image = qrCodeImage;
                    }
                }
            }
        }

        // Static method to show the form
        public static void ShowQrCode(string destination)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new QrCodeForm(destination));
        }
    }

    [XmlRoot("WorkspaceTree", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
    public class WorkspaceTree
    {
        [XmlElement("Id", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Id { get; set; }

        [XmlElement("Name", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Name { get; set; }

        [XmlArray("Pages", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        [XmlArrayItem("PageTree", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public List<PageTree> Pages { get; set; }
    }
    [XmlRoot("WorkspaceTrees", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
    public class WorkspaceTreesRoot
    {
        [XmlArray("Workspaces", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        [XmlArrayItem("WorkspaceTree", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public List<WorkspaceTree> Workspaces { get; set; }
    }
    public class PageTree
    {
        [XmlElement("Id", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Id { get; set; }

        [XmlElement("Name", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Name { get; set; }

        [XmlArray("Containers", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        [XmlArrayItem("ContainerTree", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public List<ContainerTree> Containers { get; set; }
    }
    public class ContainerTree
    {
        [XmlElement("Id", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Id { get; set; }

        [XmlElement("Name", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Name { get; set; }

        [XmlArray("Tabs", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        [XmlArrayItem("TabTree", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public List<TabTree> Tabs { get; set; }
    }
    public class TabTree
    {
        [XmlElement("Id", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Id { get; set; }

        [XmlElement("Name", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Name { get; set; }

        [XmlArray("Buttons", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        [XmlArrayItem("Button", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public List<ButtonNode> Buttons { get; set; }
    }
    public class ButtonNode
    {
        [XmlElement("BackgroundColor", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string BackgroundColor { get; set; }

        [XmlElement("Column", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public int Column { get; set; }

        [XmlElement("ForegroundColor", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string ForegroundColor { get; set; }

        [XmlElement("HasAudio", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public bool HasAudio { get; set; }

        [XmlElement("Id", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Id { get; set; }

        [XmlElement("Name", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public string Name { get; set; }

        [XmlElement("Row", Namespace = "http://standards.daktronics.com/schemas/DisplayStudio/Workspace/1_0")]
        public int Row { get; set; }
    }           
}

